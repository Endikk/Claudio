using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.ViewModels;

namespace Claudio.Core.Presentation;

/// <summary>How many agents of a project are in each state, and how many of its sessions wait, as Claudy's <c>RoomTally</c>.</summary>
public readonly record struct RoomTally
{
    public RoomTally(RoomViewModel.Project project, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(project);
        foreach (var agent in project.Agents)
        {
            switch (agent.Status(now))
            {
                case AgentStatus.Working:
                    Working++;
                    break;
                case AgentStatus.Stalled:
                    Stuck++;
                    var silence = agent.Silence(now);
                    LongestSilence = silence > LongestSilence ? silence : LongestSilence;
                    break;
                case AgentStatus.Done:
                    Done++;
                    break;
                default:
                    break;
            }
        }
        Waiting = project.Sessions.Count(session => session.IsWaiting && !session.IsBackground);
    }

    public int Working { get; }
    public int Stuck { get; }
    public int Done { get; }
    public int Waiting { get; }

    /// <summary>The longest silence among the stuck ones.</summary>
    public TimeSpan LongestSilence { get; }
}

/// <summary>What Claudy's second screen says of the session, worked out away from the UI, as Claudy's <c>RoomSummary.sentences</c>.</summary>
public static class RoomSummaryText
{
    /// <summary>The state of a project in sentences, the one that needs the user first. A line without an ink is grey.</summary>
    public static IReadOnlyList<(string Text, Ink? Ink)> Sentences(RoomTally tally, ClaudeSession? lead)
    {
        var lines = new List<(string Text, Ink? Ink)>();
        if (lead is { IsWaiting: true })
        {
            lines.Add(("Waiting for your answer" + (lead.WaitingReason is { } reason ? $": {reason.ToLowerInvariant()}" : ""), Ink.Amber));
        }
        if (tally.Stuck > 0)
        {
            var silence = AgentsViewModel.Duration(tally.LongestSilence);
            lines.Add((tally.Stuck == 1 ? $"1 agent has been silent for {silence}" : $"{tally.Stuck} agents are silent, one for {silence}", Ink.Danger));
        }
        if (tally.Working > 0)
        {
            lines.Add((tally.Working == 1 ? "1 agent is working" : $"{tally.Working} agents are working", Ink.Coral));
        }
        if (tally.Done > 0)
        {
            lines.Add((tally.Done == 1 ? "1 agent is done" : $"{tally.Done} agents are done", Ink.Positive));
        }
        if (lines.Count == 0)
        {
            lines.Add(lead?.Status switch
            {
                SessionState.Busy => ("Claude is working on its own", Ink.Coral),
                SessionState.Idle => ("Nothing is running", null),
                _ => ("No agent is running", null),
            });
        }
        return lines;
    }
}
