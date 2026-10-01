using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Claudio.Core.Services;

namespace Claudio.App;

/// <summary>
/// Claudio's own token store, the Windows counterpart of Claudy's "Claudy-credentials" keychain
/// item: a generic credential named <c>Claudio-credentials</c> in the user's Credential Manager,
/// kept on this machine and never roaming. Only the token its sign-in obtained lives here.
/// <para>
/// Claude Code's <c>.credentials.json</c> is not a store of Claudio's. It is borrowed read-only by
/// <see cref="ClaudeCodeCredentials"/>, refresh token dropped: refreshing it from here would rotate
/// Claude Code's refresh token and sign Claude Code out. It is never written.
/// </para>
/// </summary>
internal static partial class ClaudeCredentialsStore
{
    private const string Target = "Claudio-credentials";
    private const string UserName = "Claudio";
    private const string Comment = "Claudio: Claude token";
    private const uint GenericType = 1;
    /// <summary>For this user, on this machine: survives a restart, does not roam with the profile.</summary>
    private const uint PersistLocalMachine = 2;
    /// <summary><c>CRED_MAX_CREDENTIAL_BLOB_SIZE</c>: 5 × 512 bytes.</summary>
    private const int MaxBlobSize = 5 * 512;
    private const int ErrorNotFound = 1168;

    public static OwnTokenStore Store { get; } = new(Load, Persist, Erase);

    public static OAuthCredentials? Load()
    {
        var blob = Read();
        if (blob is null)
        {
            return null;
        }
        try
        {
            return OAuthCredentials.Parse(Encoding.UTF8.GetString(blob), CredentialSource.OwnStore);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    /// <summary>
    /// Writes the token back. Only the <c>claudeAiOauth</c> object is kept, which the blob's size
    /// limit leaves ample room for. Rewriting Claude Code's token would steal its session, so a
    /// borrowed one is refused outright.
    /// </summary>
    public static bool Persist(OAuthCredentials credentials)
    {
        if (credentials.IsBorrowed || credentials.Root["claudeAiOauth"] is not JsonObject oauth)
        {
            return false;
        }
        var blob = Encoding.UTF8.GetBytes(new JsonObject { ["claudeAiOauth"] = oauth.DeepClone() }.ToJsonString());
        try
        {
            if (blob.Length > MaxBlobSize)
            {
                DiagnosticLog.Append($"own token: {blob.Length} bytes, over the Credential Manager's {MaxBlobSize}");
                return false;
            }
            if (!Write(blob))
            {
                DiagnosticLog.Append($"own token: Credential Manager write failed ({Marshal.GetLastPInvokeError()})");
                return false;
            }
            return true;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(blob);
        }
    }

    /// <summary>Sign-out: removes Claudio's item. Claude Code's stores are never touched.</summary>
    public static void Erase()
    {
        if (!CredDelete(Target, GenericType, 0) && Marshal.GetLastPInvokeError() is var error and not ErrorNotFound)
        {
            DiagnosticLog.Append($"own token: Credential Manager delete failed ({error})");
        }
    }

    private static unsafe byte[]? Read()
    {
        if (!CredRead(Target, GenericType, 0, out var pointer))
        {
            return null;
        }
        try
        {
            var credential = (Credential*)pointer;
            if (credential->CredentialBlob == 0 || credential->CredentialBlobSize == 0)
            {
                return null;
            }
            return new ReadOnlySpan<byte>((void*)credential->CredentialBlob, (int)credential->CredentialBlobSize).ToArray();
        }
        finally
        {
            CredFree(pointer);
        }
    }

    private static unsafe bool Write(byte[] blob)
    {
        fixed (char* target = Target)
        fixed (char* userName = UserName)
        fixed (char* comment = Comment)
        fixed (byte* data = blob)
        {
            var credential = new Credential
            {
                Type = GenericType,
                TargetName = (nint)target,
                Comment = (nint)comment,
                CredentialBlobSize = (uint)blob.Length,
                CredentialBlob = (nint)data,
                Persist = PersistLocalMachine,
                UserName = (nint)userName,
            };
            return CredWrite(&credential, 0);
        }
    }

    /// <summary><c>CREDENTIALW</c>, blittable: strings are passed as pinned pointers.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public nint TargetName;
        public nint Comment;
        public uint LastWrittenLow;
        public uint LastWrittenHigh;
        public uint CredentialBlobSize;
        public nint CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public nint Attributes;
        public nint TargetAlias;
        public nint UserName;
    }

    [LibraryImport("advapi32.dll", EntryPoint = "CredReadW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredRead(string target, uint type, uint flags, out nint credential);

    [LibraryImport("advapi32.dll", EntryPoint = "CredWriteW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CredWrite(Credential* credential, uint flags);

    [LibraryImport("advapi32.dll", EntryPoint = "CredDeleteW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CredDelete(string target, uint type, uint flags);

    [LibraryImport("advapi32.dll")]
    private static partial void CredFree(nint buffer);
}
