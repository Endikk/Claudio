using System.Buffers;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Claudio.Core.Models;
using Claudio.Core.Services;
using Microsoft.Win32.SafeHandles;

namespace Claudio.App;

/// <summary>
/// What the Ports tab reads from Windows: the listening sockets, the process tree and the
/// environment of a process, plus the means to end one. Claudy gets the same from <c>lsof</c>,
/// <c>ps</c> and <c>sysctl</c>; here they are system calls, so no scan waits on a child process.
/// Every reader answers null rather than throwing: the scanner turns that into a reason or a
/// degraded scan.
/// </summary>
internal static unsafe partial class WindowsProcesses
{
    /// <summary>
    /// The current user's TCP sockets in the LISTEN state, IPv4 and IPv6. Claudy asks lsof for
    /// its own user's alone; here a process that cannot be opened for reading, or that runs as
    /// someone else (a service, an elevated program), is simply not ours: not a degraded scan.
    /// </summary>
    public static IReadOnlyList<Listener>? Listeners()
    {
        var sockets = new List<(int Pid, int Port, string Address)>();
        if (!ReadTcpTable(AfInet, sockets))
        {
            return null;
        }
        // IPv6 can be turned off, and then there is nothing to read: not a failure.
        ReadTcpTable(AfInet6, sockets);

        var commands = new Dictionary<int, string?>();
        var listeners = new List<Listener>();
        foreach (var (pid, port, address) in sockets)
        {
            if (!commands.TryGetValue(pid, out var command))
            {
                command = OwnImageName(pid);
                commands[pid] = command;
            }
            if (command is not null)
            {
                listeners.Add(new Listener(pid, command, port, address));
            }
        }
        return PortScanner.Collapse(listeners);
    }

    /// <summary>
    /// Every process with its parent, start time and command line. One that cannot be opened (the
    /// system's own) still appears, with its image name and no start time.
    /// </summary>
    public static ProcessTable? Table()
    {
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == InvalidHandle)
        {
            return null;
        }
        try
        {
            var processes = new Dictionary<int, RunningProcess>();
            var entry = new ProcessEntry32 { Size = (uint)sizeof(ProcessEntry32) };
            if (Process32FirstW(snapshot, &entry) == 0)
            {
                return null;
            }
            do
            {
                var pid = (int)entry.ProcessId;
                processes[pid] = Describe(pid, (int)entry.ParentProcessId, new string(entry.ExeFile));
            }
            while (Process32NextW(snapshot, &entry) != 0);
            return new ProcessTable(processes);
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>
    /// The Claude markers of a process's environment, read from its PEB: Windows has no other way
    /// to see another process's environment. The block is walked in place and wiped afterwards,
    /// since it holds tokens. Null when it cannot be read.
    /// </summary>
    public static ProcessEnvironment.Markers? Markers(int pid)
    {
        if (pid <= LastSystemPid)
        {
            return null;
        }
        using var process = OpenProcess(ProcessQueryLimitedInformation | ProcessVmRead, 0, (uint)pid);
        if (process.IsInvalid || !EnvironmentBlock(process, out var address, out var size))
        {
            return null;
        }

        // EnvironmentSize is exact on every Windows this runs on; without it, the read goes as far
        // as memory allows and the parser stops at the block's end.
        var bytes = size is > 0 and <= MaximumEnvironment ? (int)size : MaximumEnvironment;
        var chars = ArrayPool<char>.Shared.Rent(bytes / sizeof(char));
        try
        {
            fixed (char* block = chars)
            {
                if (ReadProcessMemory(process, address, block, (nuint)bytes, out var read) == 0 && read == 0)
                {
                    return null;
                }
                return ProcessEnvironment.ParseBlock(new ReadOnlySpan<char>(block, (int)read / sizeof(char)));
            }
        }
        finally
        {
            ArrayPool<char>.Shared.Return(chars, clearArray: true);
        }
    }

    /// <summary>
    /// Ends processes for <see cref="PortReaper"/>. The handle opened to terminate a process is
    /// kept until the process is seen gone: while it is held, Windows cannot hand the PID to anyone
    /// else, so the wait that follows watches the very process that was terminated.
    /// </summary>
    internal sealed class Signals : IProcessSignals, IDisposable
    {
        private readonly Dictionary<int, SafeProcessHandle> _held = [];
        private readonly Lock _lock = new();

        public bool Terminate(int pid)
        {
            if (pid <= LastSystemPid)
            {
                return false;
            }
            lock (_lock)
            {
                // A second attempt goes through the handle of the first, never through the PID again.
                if (_held.TryGetValue(pid, out var held))
                {
                    return WaitForSingleObject(held, 0) != WaitTimeout || TerminateProcess(held, 1) != 0;
                }
                Release(exitedOnly: true);
                var process = OpenProcess(ProcessTerminate | Synchronize | ProcessQueryLimitedInformation, 0, (uint)pid);
                if (process.IsInvalid)
                {
                    process.Dispose();
                    return false;
                }
                _held[pid] = process;
                return TerminateProcess(process, 1) != 0;
            }
        }

        public bool IsRunning(int pid)
        {
            lock (_lock)
            {
                if (_held.TryGetValue(pid, out var held))
                {
                    if (WaitForSingleObject(held, 0) == WaitTimeout)
                    {
                        return true;
                    }
                    held.Dispose();
                    _held.Remove(pid);
                    return false;
                }
            }
            using var process = OpenProcess(Synchronize | ProcessQueryLimitedInformation, 0, (uint)pid);
            if (process.IsInvalid)
            {
                // No such process; anything else (access denied) means it exists.
                return Marshal.GetLastPInvokeError() != ErrorInvalidParameter;
            }
            return WaitForSingleObject(process, 0) == WaitTimeout;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                Release(exitedOnly: false);
            }
        }

        private void Release(bool exitedOnly)
        {
            foreach (var (pid, handle) in _held.ToList())
            {
                if (!exitedOnly || WaitForSingleObject(handle, 0) != WaitTimeout)
                {
                    handle.Dispose();
                    _held.Remove(pid);
                }
            }
        }
    }

    private static bool ReadTcpTable(uint family, List<(int Pid, int Port, string Address)> sockets)
    {
        uint size = 0;
        byte[]? buffer = null;
        // The table can grow between the size query and the read: ask again a few times.
        for (var attempt = 0; attempt < 4; attempt++)
        {
            fixed (byte* table = buffer)
            {
                var status = GetExtendedTcpTable(table, ref size, 0, family, TcpTableOwnerPidListener, 0);
                if (status == ErrorInsufficientBuffer)
                {
                    buffer = new byte[size + 1024];
                    size = (uint)buffer.Length;
                    continue;
                }
                if (status != 0 || table == null)
                {
                    return false;
                }

                var count = *(uint*)table;
                var rowSize = family == AfInet ? Tcp4RowSize : Tcp6RowSize;
                if (4 + ((long)count * rowSize) > buffer!.Length)
                {
                    return false;
                }
                for (var index = 0; index < count; index++)
                {
                    var row = table + 4 + (index * rowSize);
                    sockets.Add(family == AfInet
                        ? ((int)*(uint*)(row + 20), Port(*(uint*)(row + 8)), new IPAddress(new ReadOnlySpan<byte>(row + 4, 4)).ToString())
                        : ((int)*(uint*)(row + 52), Port(*(uint*)(row + 20)), new IPAddress(new ReadOnlySpan<byte>(row, 16), *(uint*)(row + 16)).ToString()));
                }
                return true;
            }
        }
        return false;
    }

    /// <summary>The port sits in the low word, in network order.</summary>
    private static int Port(uint value) => (int)(((value & 0xFF) << 8) | ((value >> 8) & 0xFF));

    /// <summary>The image name (<c>node.exe</c>) of a process of the current user, null for anyone else's.</summary>
    private static string? OwnImageName(int pid)
    {
        if (pid <= LastSystemPid)
        {
            return null;
        }
        using var process = OpenProcess(ProcessQueryLimitedInformation | ProcessVmRead, 0, (uint)pid);
        if (process.IsInvalid || !IsCurrentUser(process))
        {
            return null;
        }
        return ImagePath(process) is { } path ? Path.GetFileName(path) : $"PID {pid}";
    }

    private static readonly SecurityIdentifier? CurrentUser = CurrentUserSid();

    private static SecurityIdentifier? CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User;
    }

    private static bool IsCurrentUser(SafeProcessHandle process)
    {
        if (CurrentUser is null || OpenProcessToken(process, TokenQuery, out var token) == 0)
        {
            return false;
        }
        try
        {
            // TOKEN_USER: a SID pointer into the same buffer, then the SID itself (68 bytes at most).
            const int Length = 256;
            var buffer = stackalloc byte[Length];
            return GetTokenInformation(token, TokenUserClass, buffer, Length, out _) != 0
                && new SecurityIdentifier(*(nint*)buffer).Equals(CurrentUser);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static RunningProcess Describe(int pid, int parent, string imageName)
    {
        using var process = OpenProcess(ProcessQueryLimitedInformation, 0, (uint)pid);
        if (process.IsInvalid)
        {
            return new RunningProcess(pid, parent, DateTimeOffset.MinValue, imageName);
        }
        var startedAt = GetProcessTimes(process, out var creation, out _, out _, out _) != 0 && creation > 0
            ? new DateTimeOffset(DateTime.FromFileTimeUtc(creation), TimeSpan.Zero)
            : DateTimeOffset.MinValue;
        return new RunningProcess(pid, parent, startedAt, CommandLine(process) ?? ImagePath(process) ?? imageName);
    }

    /// <summary>
    /// <c>ProcessCommandLineInformation</c> asks only for the limited query right, unlike reading
    /// the command line out of the PEB.
    /// </summary>
    private static string? CommandLine(SafeProcessHandle process)
    {
        const int Inline = 2048;
        var inline = stackalloc byte[Inline];
        var status = NtQueryInformationProcess(process, ProcessCommandLineInformation, inline, Inline, out var needed);
        if (status >= 0)
        {
            return Text((UnicodeString*)inline);
        }
        if (status is not (StatusInfoLengthMismatch or StatusBufferTooSmall or StatusBufferOverflow) || needed is 0 or > 1 << 20)
        {
            return null;
        }
        var buffer = new byte[needed];
        fixed (byte* data = buffer)
        {
            return NtQueryInformationProcess(process, ProcessCommandLineInformation, data, needed, out _) >= 0
                ? Text((UnicodeString*)data)
                : null;
        }

        static string? Text(UnicodeString* text) =>
            text->Buffer is null || text->Length == 0 ? null : new string(text->Buffer, 0, text->Length / sizeof(char));
    }

    private static string? ImagePath(SafeProcessHandle process)
    {
        const int Capacity = 1024;
        var path = stackalloc char[Capacity];
        var size = (uint)Capacity;
        return QueryFullProcessImageName(process, 0, path, ref size) != 0 ? new string(path, 0, (int)size) : null;
    }

    /// <summary>
    /// Where the environment block lives: PEB, then its process parameters, then the block and its
    /// size. A 32-bit process under WOW64 has its own 32-bit PEB with a 32-bit layout. x64 and
    /// ARM64 share the 64-bit one; offsets are those of every Windows since Vista.
    /// </summary>
    private static bool EnvironmentBlock(SafeProcessHandle process, out nuint address, out ulong size)
    {
        address = 0;
        size = 0;
        nuint peb32 = 0;
        if (NtQueryInformationProcess(process, ProcessWow64Information, &peb32, (uint)sizeof(nuint), out _) < 0)
        {
            return false;
        }
        if (peb32 != 0)
        {
            uint parameters32 = 0, environment32 = 0, size32 = 0;
            if (!Read(process, peb32 + Peb32ProcessParameters, &parameters32, sizeof(uint)) || parameters32 == 0
                || !Read(process, parameters32 + Parameters32Environment, &environment32, sizeof(uint))
                || !Read(process, parameters32 + Parameters32EnvironmentSize, &size32, sizeof(uint)))
            {
                return false;
            }
            address = environment32;
            size = size32;
            return address != 0;
        }

        ProcessBasicInformation basic;
        if (NtQueryInformationProcess(process, ProcessBasicInformationClass, &basic, (uint)sizeof(ProcessBasicInformation), out _) < 0
            || basic.PebBaseAddress == 0)
        {
            return false;
        }
        nuint parameters = 0, environment = 0;
        ulong environmentSize = 0;
        if (!Read(process, basic.PebBaseAddress + Peb64ProcessParameters, &parameters, (uint)sizeof(nuint)) || parameters == 0
            || !Read(process, parameters + Parameters64Environment, &environment, (uint)sizeof(nuint))
            || !Read(process, parameters + Parameters64EnvironmentSize, &environmentSize, sizeof(ulong)))
        {
            return false;
        }
        address = environment;
        size = environmentSize;
        return address != 0;
    }

    private static bool Read(SafeProcessHandle process, nuint address, void* value, uint length) =>
        ReadProcessMemory(process, address, value, length, out var read) != 0 && read == length;

    private const int LastSystemPid = 4;
    private const int MaximumEnvironment = 1 << 20;

    private const uint ProcessTerminate = 0x0001;
    private const uint ProcessVmRead = 0x0010;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint Synchronize = 0x00100000;
    private const uint TokenQuery = 0x0008;
    private const int TokenUserClass = 1;
    private const uint Th32csSnapProcess = 0x00000002;
    private const uint AfInet = 2;
    private const uint AfInet6 = 23;
    private const int TcpTableOwnerPidListener = 3;
    private const int Tcp4RowSize = 24; // MIB_TCPROW_OWNER_PID
    private const int Tcp6RowSize = 56; // MIB_TCP6ROW_OWNER_PID
    private const uint ErrorInsufficientBuffer = 122;
    private const int ErrorInvalidParameter = 87;
    private const uint WaitTimeout = 0x102;
    private const nint InvalidHandle = -1;

    private const int ProcessBasicInformationClass = 0;
    private const int ProcessWow64Information = 26;
    private const int ProcessCommandLineInformation = 60;
    private const int StatusInfoLengthMismatch = unchecked((int)0xC0000004);
    private const int StatusBufferTooSmall = unchecked((int)0xC0000023);
    private const int StatusBufferOverflow = unchecked((int)0x80000005);

    private const uint Peb64ProcessParameters = 0x20;
    private const uint Parameters64Environment = 0x80;
    private const uint Parameters64EnvironmentSize = 0x3F0;
    private const uint Peb32ProcessParameters = 0x10;
    private const uint Parameters32Environment = 0x48;
    private const uint Parameters32EnvironmentSize = 0x290;

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nuint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public nint ExitStatus;
        public nuint PebBaseAddress;
        public nuint AffinityMask;
        public nint BasePriority;
        public nuint UniqueProcessId;
        public nuint InheritedFromUniqueProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString
    {
        public ushort Length;
        public ushort MaximumLength;
        public char* Buffer;
    }

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedTcpTable(byte* table, ref uint size, int order, uint addressFamily, int tableClass, uint reserved);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int Process32FirstW(nint snapshot, ProcessEntry32* entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int Process32NextW(nint snapshot, ProcessEntry32* entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, int inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int GetProcessTimes(SafeProcessHandle process, out long creation, out long exit, out long kernel, out long user);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    private static partial int QueryFullProcessImageName(SafeProcessHandle process, uint flags, char* name, ref uint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int ReadProcessMemory(SafeProcessHandle process, nuint address, void* buffer, nuint size, out nuint read);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial int TerminateProcess(SafeProcessHandle process, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int OpenProcessToken(SafeProcessHandle process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    private static partial int GetTokenInformation(nint token, int informationClass, void* information, uint length, out uint returnLength);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(SafeProcessHandle process, int informationClass, void* information, uint length, out uint returnLength);
}
