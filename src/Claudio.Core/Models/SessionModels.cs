namespace Claudio.Core.Models;

/// <summary>
/// The app a Claude Code session runs in, found up its process tree: what "Open" brings forward,
/// as Claudy's <c>HostApp</c>. A Mac names the app by its bundle; Windows by the program that owns
/// the window, so <see cref="Pid"/> is the process whose window comes forward.
/// </summary>
public sealed record HostApp(string Name, int Pid)
{
    /// <summary>The name for a button: "VS Code" rather than its full name.</summary>
    public string ShortName => Name switch
    {
        "Visual Studio Code" => "VS Code",
        "Visual Studio Code - Insiders" => "VS Code Insiders",
        _ => Name,
    };
}

public enum SessionState
{
    Busy,
    Idle,
    /// <summary>Claude Code asks the user something; <see cref="ClaudeSession.WaitingFor"/> is its own words for it.</summary>
    Waiting,
    /// <summary>A Claude Code too old to write its status.</summary>
    Unknown,
}

/// <summary>
/// An open Claude Code session, as its session file and its process describe it, as Claudy's
/// <c>ClaudeSession</c>.
///
/// Claude Code rewrites <c>&lt;config&gt;/sessions/&lt;pid&gt;.json</c> as the session changes:
/// <c>busy</c> while it works, <c>idle</c> at its prompt, <c>waiting</c> when it asks the user
/// something, with what it waits for in <c>waitingFor</c> ("permission prompt", "input needed").
/// Nothing is installed in Claude Code to know it.
/// </summary>
public sealed record ClaudeSession
{
    public required string Id { get; init; }
    public required int Pid { get; init; }

    /// <summary>
    /// When the process started, as the session file records it: a pid is reused once its process
    /// exits, the start time is not.
    /// </summary>
    public DateTimeOffset? ProcessStartedAt { get; init; }

    /// <summary><c>claudy-a5</c>, or the working folder's name when the session has none.</summary>
    public required string Name { get; init; }

    public string? Cwd { get; init; }

    /// <summary>When the session opened.</summary>
    public DateTimeOffset? OpenedAt { get; init; }

    public SessionState Status { get; init; } = SessionState.Unknown;

    /// <summary>What it waits for, in Claude Code's words, while <see cref="Status"/> is waiting.</summary>
    public string? WaitingFor { get; init; }

    /// <summary>When the status last changed: how long it has been waiting, or idle.</summary>
    public DateTimeOffset? StatusSince { get; init; }

    /// <summary>
    /// Launched by a program through the Agent SDK rather than by someone: nobody sits at it, so it
    /// is only listed while it runs agents, and never said to wait for anyone.
    /// </summary>
    public bool IsBackground { get; init; }

    public HostApp? Host { get; init; }

    /// <summary>The last thing the user asked it, on one line.</summary>
    public string? LastPrompt { get; init; }

    /// <summary>The session's own last tool call, apart from what its agents do.</summary>
    public AgentAction? LastAction { get; init; }

    /// <summary>The session file's status: anything new from a newer Claude Code reads as busy.</summary>
    public static SessionState StateOf(string? raw) => raw switch
    {
        "busy" => SessionState.Busy,
        "idle" => SessionState.Idle,
        "waiting" => SessionState.Waiting,
        null => SessionState.Unknown,
        _ => SessionState.Busy,
    };

    public bool IsWaiting => Status == SessionState.Waiting;

    /// <summary>
    /// Ctrl+C means something only while it works or asks: at its prompt there is no turn to stop.
    /// It also takes the process's start time: without it, a pid reused after the session exits
    /// cannot be told apart from the session.
    /// </summary>
    public bool CanBeInterrupted => ProcessStartedAt is not null && Status != SessionState.Idle;

    public string? FolderName
    {
        get
        {
            if (Cwd is null)
            {
                return null;
            }
            var trimmed = Cwd.TrimEnd('/', '\\');
            var name = trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
            return name.Length == 0 ? null : name;
        }
    }

    /// <summary>What it waits for, in a sentence's first words: "Permission prompt", "Input needed".</summary>
    public string? WaitingReason
    {
        get
        {
            if (Status != SessionState.Waiting || string.IsNullOrEmpty(WaitingFor))
            {
                return null;
            }
            return char.ToUpperInvariant(WaitingFor[0]) + WaitingFor[1..];
        }
    }

    /// <summary>The session file's <c>entrypoint</c> for a program driving Claude Code: <c>sdk-cli</c>, <c>sdk-ts</c>.</summary>
    public static bool IsBackgroundEntrypoint(string? entrypoint) =>
        entrypoint?.StartsWith("sdk", StringComparison.Ordinal) ?? false;
}

/// <summary>One pass over Claude Code's files: the sessions open on this PC and the agents they run.</summary>
public sealed record ClaudeActivity
{
    public ValueList<ClaudeSession> Sessions { get; init; } = ValueList<ClaudeSession>.Empty;
    public ValueList<RunningAgent> Agents { get; init; } = ValueList<RunningAgent>.Empty;

    public static ClaudeActivity Empty { get; } = new();
}

/// <summary>A session and its agents, as the Sessions tab lists them.</summary>
public sealed record SessionGroup(ClaudeSession Session, ValueList<RunningAgent> Agents)
{
    public string Id => Session.Id;
}
