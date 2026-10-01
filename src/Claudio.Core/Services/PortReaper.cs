using System.Diagnostics;
using Claudio.Core.Models;

namespace Claudio.Core.Services;

public enum KillRefusal
{
    /// <summary>The PID now belongs to another process, or to none.</summary>
    IdentityChanged,
    /// <summary>Claudio itself, one of its ancestors, or the system.</summary>
    ProtectedProcess,
    /// <summary>A Claude Code session that is still running.</summary>
    LiveClaudeSession,
    SystemRefused,
    SurvivedKill,
}

/// <summary>
/// The side effects of a kill, behind an interface so the guardrails can be tested without ever
/// touching a real process. Windows has no SIGTERM and no process group: a process is terminated
/// outright, and its children go one by one.
/// </summary>
public interface IProcessSignals
{
    /// <summary>Ends the process; false when the system refused or it could not be opened.</summary>
    bool Terminate(int pid);

    /// <summary>A process that exists but cannot be opened is running, not absent.</summary>
    bool IsRunning(int pid);
}

/// <summary>
/// Kills a port's process, or refuses and says why, as Claudy's <c>PortReaper</c>.
///
/// Every refusal is a case where killing would be worse than leaving the port open: ending the
/// wrong process after a PID reuse, taking down Claudio, or taking down the Claude session the
/// user is talking to. A dev server is usually a tree, so its descendants in the table go first,
/// deepest first, except any that is itself protected, which keeps its own subtree.
/// </summary>
public sealed class PortReaper
{
    private static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(100);
    private const int MaximumDepth = 64;
    private const int LastSystemPid = 4;

    private readonly IProcessSignals _signals;
    private readonly int _ownPid;
    private readonly TimeSpan _grace;

    public PortReaper(IProcessSignals signals, int ownPid, TimeSpan? grace = null)
    {
        ArgumentNullException.ThrowIfNull(signals);
        _signals = signals;
        _ownPid = ownPid;
        _grace = grace ?? DefaultGrace;
    }

    /// <summary>Null when the process is gone; otherwise why it was left alone or survived.</summary>
    public KillRefusal? Kill(ListeningPort port, ProcessTable table)
    {
        ArgumentNullException.ThrowIfNull(port);
        ArgumentNullException.ThrowIfNull(table);

        if (port.Pid <= LastSystemPid || port.Pid == _ownPid)
        {
            return KillRefusal.ProtectedProcess;
        }
        // Between the scan and the click the process may have died and its PID been reissued.
        // The start time is what tells the two apart.
        if (!table.Processes.TryGetValue(port.Pid, out var process) || process.StartedAt != port.StartedAt)
        {
            return KillRefusal.IdentityChanged;
        }
        var ownAncestors = table.Ancestors(_ownPid).Select(ancestor => ancestor.Pid).ToHashSet();
        if (ownAncestors.Contains(port.Pid))
        {
            return KillRefusal.ProtectedProcess;
        }
        if (ProcessTable.IsClaudeBinary(process.Arguments) && _signals.IsRunning(port.Pid))
        {
            return KillRefusal.LiveClaudeSession;
        }

        var descendants = new List<int>();
        CollectDescendants(port.Pid, table, ownAncestors, descendants, [port.Pid], depth: 0);
        foreach (var descendant in descendants)
        {
            _signals.Terminate(descendant);
        }

        // A second termination is the only escalation Windows has. It goes to the port's process
        // alone: a child that is gone may have handed its PID on already.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!_signals.Terminate(port.Pid))
            {
                return _signals.IsRunning(port.Pid) ? KillRefusal.SystemRefused : null;
            }
            if (WaitForExit(port.Pid))
            {
                return null;
            }
        }
        return KillRefusal.SurvivedKill;
    }

    /// <summary>Post-order: a process is listed after everything below it.</summary>
    private void CollectDescendants(int pid, ProcessTable table, HashSet<int> ownAncestors,
                                    List<int> found, HashSet<int> seen, int depth)
    {
        if (depth >= MaximumDepth)
        {
            return;
        }
        foreach (var child in table.Children(pid))
        {
            if (!seen.Add(child.Pid) || IsProtected(child, ownAncestors))
            {
                continue;
            }
            CollectDescendants(child.Pid, table, ownAncestors, found, seen, depth + 1);
            found.Add(child.Pid);
        }
    }

    private bool IsProtected(RunningProcess process, HashSet<int> ownAncestors) =>
        process.Pid <= LastSystemPid || process.Pid == _ownPid || ownAncestors.Contains(process.Pid)
        || ProcessTable.IsClaudeBinary(process.Arguments);

    private bool WaitForExit(int pid)
    {
        var clock = Stopwatch.StartNew();
        while (_signals.IsRunning(pid))
        {
            var remaining = _grace - clock.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                return false;
            }
            Thread.Sleep(remaining < Poll ? remaining : Poll);
        }
        return true;
    }
}
