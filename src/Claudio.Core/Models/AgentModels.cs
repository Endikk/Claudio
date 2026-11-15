namespace Claudio.Core.Models;

/// <summary>
/// What one tool call did, reduced to what the agents list tells apart: writing a file stands
/// out, everything else is background work.
/// </summary>
public enum AgentActionKind
{
    Read,
    Edit,
    Run,
    Web,
    Other,
}

public static class AgentActionKinds
{
    private static readonly string[] Browsing = ["browser", "chrome", "devtools", "playwright"];

    public static AgentActionKind Of(string tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        switch (tool)
        {
            case "Read" or "Grep" or "Glob" or "LS" or "NotebookRead":
                return AgentActionKind.Read;
            case "Edit" or "Write" or "MultiEdit" or "NotebookEdit":
                return AgentActionKind.Edit;
            case "Bash" or "BashOutput" or "KillShell" or "KillBash" or "PowerShell":
                return AgentActionKind.Run;
            case "WebFetch" or "WebSearch":
                return AgentActionKind.Web;
            default:
                // Browser tools come through MCP servers, under names their plugin chooses.
                var browsing = Browsing.Any(word => tool.Contains(word, StringComparison.Ordinal));
                return tool.StartsWith("mcp__", StringComparison.Ordinal) && browsing ? AgentActionKind.Web : AgentActionKind.Other;
        }
    }
}

/// <summary>One tool call from an agent's transcript.</summary>
public sealed record AgentAction
{
    public AgentAction(DateTimeOffset date, AgentActionKind kind, string tool, string? file, AgentWork? work = null, string? detail = null)
    {
        Date = date;
        Kind = kind;
        Tool = tool;
        File = file;
        Work = work ?? AgentWorks.Read(tool, path: file).Work;
        Detail = detail;
    }

    public DateTimeOffset Date { get; init; }
    public AgentActionKind Kind { get; init; }
    public string Tool { get; init; }

    /// <summary>The file's name alone: its full path says nothing more in a 300-point card.</summary>
    public string? File { get; init; }

    /// <summary>What the call is for, as the agent room shows it.</summary>
    public AgentWork Work { get; init; }

    /// <summary>The few words that name it: a command's first words, a skill's name, an MCP server's.</summary>
    public string? Detail { get; init; }
}

/// <summary>One fifteen-second slot of an agent's track.</summary>
public readonly record struct AgentTrackSlot(int Edits = 0, int Others = 0);

/// <summary>
/// <c>Idle</c> is a pause: a long command, a wait on the parent. <c>Stalled</c> is a pause long
/// enough to go and look: a permission nobody answered, a hung tool.
/// </summary>
public enum AgentStatus
{
    Working,
    Idle,
    Stalled,
    Done,
}

/// <summary>
/// A subagent Claude Code launched, as the agents list shows it, as Claudy's <c>RunningAgent</c>.
/// Built by <c>AgentScanner</c> from the agent's own transcript, so it says only what Claude Code
/// wrote down: no progress percentage, which no file holds.
/// </summary>
public sealed record RunningAgent
{
    public required string Id { get; init; }
    public required string SessionId { get; init; }

    /// <summary>
    /// The Claude Code process that launched it. Agents have no process of their own: stopping one
    /// means interrupting its session, as Ctrl+C would.
    /// </summary>
    public required int SessionPid { get; init; }

    /// <summary>
    /// When that process started, as its session file records it: a pid is reused once its process
    /// exits, the start time is not.
    /// </summary>
    public DateTimeOffset? SessionStartedAt { get; init; }

    /// <summary><c>claire-bb</c>, or the working folder's name when the session has none.</summary>
    public required string SessionName { get; init; }

    /// <summary>What it was asked, as written when it was launched.</summary>
    public required string Title { get; init; }

    public required DateTimeOffset StartedAt { get; init; }

    /// <summary>Last write to its transcript.</summary>
    public required DateTimeOffset LastActivity { get; init; }

    public bool IsFinished { get; init; }
    public int ActionCount { get; init; }

    /// <summary>Tool calls of the last <see cref="TrackSpan"/>, oldest first.</summary>
    public ValueList<AgentAction> Recent { get; init; } = ValueList<AgentAction>.Empty;

    public AgentAction? LastAction { get; init; }

    /// <summary>Files it wrote, in the order it first touched them.</summary>
    public ValueList<string> EditedFiles { get; init; } = ValueList<string>.Empty;

    /// <summary>Past this, a silent agent is shown at rest: waiting on a long command, or on its parent.</summary>
    public static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(60);

    /// <summary>Past this, its silence is called out.</summary>
    public static readonly TimeSpan StalledAfter = TimeSpan.FromSeconds(180);

    /// <summary>The track's reach, cut in <see cref="TrackSlots"/> slots.</summary>
    public static readonly TimeSpan TrackSpan = TimeSpan.FromSeconds(600);

    public const int TrackSlots = 40;

    /// <summary>How far back the room looks to decide where an agent sits.</summary>
    public static readonly TimeSpan FamilySpan = TimeSpan.FromSeconds(90);

    public AgentStatus Status(DateTimeOffset now)
    {
        if (IsFinished)
        {
            return AgentStatus.Done;
        }
        var silence = Silence(now);
        return silence < IdleAfter ? AgentStatus.Working
            : silence < StalledAfter ? AgentStatus.Idle
            : AgentStatus.Stalled;
    }

    /// <summary>How long since it last wrote anything.</summary>
    public TimeSpan Silence(DateTimeOffset now) => now - LastActivity > TimeSpan.Zero ? now - LastActivity : TimeSpan.Zero;

    /// <summary>What it is touching now: the file of its last call, or the kind of work in one word.</summary>
    public string Touching
    {
        get
        {
            if (LastAction is not { } action)
            {
                return "starting";
            }
            if (action.File is { } file)
            {
                return file;
            }
            return action.Kind switch
            {
                AgentActionKind.Run => "terminal",
                AgentActionKind.Web => "web",
                AgentActionKind.Read => "search",
                _ => action.Tool.ToLowerInvariant(),
            };
        }
    }

    /// <summary>
    /// The area of the room its recent work belongs to: the family most of its last calls fall in,
    /// the latest one on a tie. An agent reads and writes in turns; one call does not make it cross
    /// the room.
    /// </summary>
    public WorkFamily? Family(DateTimeOffset now)
    {
        var cutoff = now - FamilySpan;
        var counts = new Dictionary<WorkFamily, int>();
        WorkFamily? latest = null;
        foreach (var action in Recent)
        {
            if (action.Date < cutoff || action.Work.Family() is not { } family)
            {
                continue;
            }
            counts[family] = counts.GetValueOrDefault(family) + 1;
            latest = family;
        }
        if (counts.Count == 0)
        {
            return LastAction?.Work.Family();
        }
        var top = counts.Values.Max();
        if (latest is { } last && counts[last] == top)
        {
            return last;
        }
        return AgentWorks.Families.First(family => counts.GetValueOrDefault(family) == top);
    }

    /// <summary>What it does now in a few words: "tests · swift test", "code · AgentScanner.swift".</summary>
    public string WorkLabel
    {
        get
        {
            if (LastAction is not { } action)
            {
                return "starting";
            }
            var detail = action.Detail ?? action.File;
            return detail is null ? action.Work.Label() : $"{action.Work.Label()} · {detail}";
        }
    }

    /// <summary>Its last call wrote a file: the file is shown in the accent.</summary>
    public bool IsWriting => LastAction is { Kind: AgentActionKind.Edit, File: not null };

    /// <summary>
    /// The last <see cref="TrackSpan"/> in slots, newest last: writes apart from the rest, so a
    /// burst of edits stands out.
    /// </summary>
    public IReadOnlyList<AgentTrackSlot> Track(DateTimeOffset now)
    {
        var slots = new AgentTrackSlot[TrackSlots];
        var width = TrackSpan.TotalSeconds / TrackSlots;
        var start = now - TrackSpan;
        foreach (var action in Recent)
        {
            // A call stamped this very second, or a moment ahead of this PC's clock, is the newest.
            var index = Math.Min((int)Math.Floor((action.Date - start).TotalSeconds / width), TrackSlots - 1);
            if (index < 0)
            {
                continue;
            }
            slots[index] = action.Kind == AgentActionKind.Edit
                ? slots[index] with { Edits = slots[index].Edits + 1 }
                : slots[index] with { Others = slots[index].Others + 1 };
        }
        return slots;
    }
}
