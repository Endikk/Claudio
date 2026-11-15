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

/// <summary>What a kill takes down besides the process it names, as Claudy's <c>KillScope</c>.</summary>
public enum KillScope
{
    /// <summary>
    /// A port's server and the processes under it, except any that is itself protected, which
    /// keeps its own subtree. Claudy signals the process group here; Windows has none.
    /// </summary>
    Group,

    /// <summary>
    /// An MCP server and every process under it, refused outright when a live Claude Code sits
    /// above or below it. Claudy uses the group only when the server leads it, since the servers
    /// keep the group of the session that died; Windows has no group, and nothing outside the
    /// tree the table shows under the server is ever touched.
    /// </summary>
    Tree,
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
/// Kills a process Claude left behind, or refuses and says why, as Claudy's <c>PortReaper</c>.
///
/// Every refusal is a case where killing would be worse than leaving the process running: ending
/// the wrong process after a PID reuse, taking down Claudio, or taking down the Claude session the
/// user is talking to. A dev server is usually a tree, so its descendants in the table go first,
/// deepest first. Claudy's SIGTERM then SIGKILL becomes one termination, then a second one for
/// whatever is still running: Windows has no gentler request to make of a console process.
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
        return Kill(port.Pid, port.StartedAt, table, KillScope.Group);
    }

    /// <summary>An MCP server goes with its whole tree.</summary>
    public KillRefusal? Kill(McpOrphan server, ProcessTable table)
    {
        ArgumentNullException.ThrowIfNull(server);
        return Kill(server.Pid, server.StartedAt, table, KillScope.Tree);
    }

    /// <summary>
    /// The process is named by its PID and start time together: the PID alone may have been
    /// handed to another process since the scan. <paramref name="table"/> is read at the click.
    /// </summary>
    public KillRefusal? Kill(int pid, DateTimeOffset startedAt, ProcessTable table, KillScope scope = KillScope.Group)
    {
        ArgumentNullException.ThrowIfNull(table);
        if (Refusal(pid, startedAt, table, scope) is { } refusal)
        {
            return refusal;
        }
        return scope == KillScope.Tree ? KillTree(pid, table) : KillGroup(pid, table);
    }

    private KillRefusal? Refusal(int pid, DateTimeOffset startedAt, ProcessTable table, KillScope scope)
    {
        if (pid <= LastSystemPid || pid == _ownPid)
        {
            return KillRefusal.ProtectedProcess;
        }
        // Between the scan and the click the process may have died and its PID been reissued.
        // The start time is what tells the two apart.
        if (!table.Processes.TryGetValue(pid, out var process) || process.StartedAt != startedAt)
        {
            return KillRefusal.IdentityChanged;
        }
        if (table.Ancestors(_ownPid).Any(ancestor => ancestor.Pid == pid))
        {
            return KillRefusal.ProtectedProcess;
        }
        if (IsLiveClaude(process))
        {
            return KillRefusal.LiveClaudeSession;
        }
        if (scope != KillScope.Tree)
        {
            return null;
        }
        // The scan found no live session above the server. Checked again on the fresh table: the
        // tree may have changed between the render and the click.
        if (table.ClaudeSessionRoot(pid) is not null)
        {
            return KillRefusal.LiveClaudeSession;
        }
        // A server that drives Claude Code holds a live session somewhere in its tree.
        var tree = new List<int>();
        CollectDescendants(pid, table, tree, [pid], depth: 0, isProtected: null);
        return tree.Any(member => IsLiveClaude(table.Processes[member])) ? KillRefusal.LiveClaudeSession : null;
    }

    private bool IsLiveClaude(RunningProcess process) =>
        ProcessTable.IsClaudeBinary(process.Arguments) && _signals.IsRunning(process.Pid);

    private KillRefusal? KillGroup(int pid, ProcessTable table)
    {
        var ownAncestors = table.Ancestors(_ownPid).Select(ancestor => ancestor.Pid).ToHashSet();
        var descendants = new List<int>();
        CollectDescendants(pid, table, descendants, [pid], depth: 0,
                           isProtected: process => IsProtected(process, ownAncestors));
        foreach (var descendant in descendants)
        {
            _signals.Terminate(descendant);
        }

        // A second termination is the only escalation Windows has. It goes to the port's process
        // alone: a child that is gone may have handed its PID on already.
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (!_signals.Terminate(pid))
            {
                return _signals.IsRunning(pid) ? KillRefusal.SystemRefused : null;
            }
            if (WaitForExit([pid]))
            {
                return null;
            }
        }
        return KillRefusal.SurvivedKill;
    }

    /// <summary>
    /// The tree goes deepest first, the server last, and the whole of it is waited for. Only the
    /// server's termination is required: a descendant may well have exited with its parent, and
    /// one already gone is not terminated again.
    /// </summary>
    private KillRefusal? KillTree(int pid, ProcessTable table)
    {
        var members = new List<int>();
        CollectDescendants(pid, table, members, [pid], depth: 0, isProtected: null);
        members.RemoveAll(member => member <= LastSystemPid || member == _ownPid);
        members.Add(pid);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var isRootRefused = false;
            foreach (var member in members.Where(_signals.IsRunning).ToList())
            {
                if (!_signals.Terminate(member) && member == pid)
                {
                    isRootRefused = true;
                }
            }
            if (isRootRefused && _signals.IsRunning(pid))
            {
                return KillRefusal.SystemRefused;
            }
            if (WaitForExit(members))
            {
                return null;
            }
        }
        return KillRefusal.SurvivedKill;
    }

    /// <summary>
    /// Post-order: a process is listed after everything below it. A protected process is left
    /// out with its whole subtree.
    /// </summary>
    private static void CollectDescendants(int pid, ProcessTable table, List<int> found, HashSet<int> seen, int depth,
                                           Func<RunningProcess, bool>? isProtected)
    {
        if (depth >= MaximumDepth)
        {
            return;
        }
        foreach (var child in table.Children(pid))
        {
            if (!seen.Add(child.Pid) || (isProtected?.Invoke(child) ?? false))
            {
                continue;
            }
            CollectDescendants(child.Pid, table, found, seen, depth + 1, isProtected);
            found.Add(child.Pid);
        }
    }

    private bool IsProtected(RunningProcess process, HashSet<int> ownAncestors) =>
        process.Pid <= LastSystemPid || process.Pid == _ownPid || ownAncestors.Contains(process.Pid)
        || ProcessTable.IsClaudeBinary(process.Arguments);

    private bool WaitForExit(IReadOnlyList<int> pids)
    {
        var clock = Stopwatch.StartNew();
        while (pids.Any(_signals.IsRunning))
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
