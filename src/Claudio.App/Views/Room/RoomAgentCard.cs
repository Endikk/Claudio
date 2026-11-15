using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Claudio.App;

/// <summary>
/// What a click on a desk opens, as Claudy's <c>RoomAgentCard</c>: the agent that sits there. What
/// it was asked, what it does now, its last calls and the files it wrote.
/// </summary>
internal sealed partial class RoomAgentCard : Grid
{
    public const double CardWidth = 236;

    /// <summary>Room kept for it when it is placed: it is never taller.</summary>
    public const double CardHeight = 200;

    private const int CallCount = 4;
    private const int FileCount = 4;

    private readonly AgentsViewModel _agents;
    private readonly Action _close;
    private readonly Border _card = new() { Padding = new Thickness(12) };
    private string? _key;

    public RoomAgentCard(AgentsViewModel agents, Action close)
    {
        _agents = agents;
        _close = close;
        Width = CardWidth;
        RoomSummary.Surface(_card);
        Children.Add(_card);
    }

    /// <summary>The agent it shows.</summary>
    public string? AgentId { get; private set; }

    public void Update(RunningAgent agent)
    {
        AgentId = agent.Id;
        var status = Status(agent);
        var calls = Calls(agent);
        var key = $"{agent.Title}|{status}|{_agents.Elapsed(agent)}|{agent.SessionName}|{agent.WorkLabel}|{string.Join(';', calls)}|{string.Join(';', agent.EditedFiles)}|{_agents.Tint(agent)}";
        if (key == _key)
        {
            return;
        }
        _key = key;
        var ink = RoomSummary.Ink;
        var quiet = RoomSummary.Quiet;

        var swatch = new Border
        {
            Width = 9,
            Height = 9,
            CornerRadius = new CornerRadius(2),
            Background = Ui.Brush(_agents.Tint(agent)),
            Margin = new Thickness(0, 3, 0, 0),
            VerticalAlignment = VerticalAlignment.Top,
        };
        var title = Ui.Text(agent.Title, 13, Ui.SemiBold, 1, ink);
        title.TextWrapping = TextWrapping.Wrap;
        title.MaxLines = 2;
        var cross = new Border
        {
            Child = Ui.Icon(RoomGlyphs.Close, 8, 1, quiet),
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Ui.Brush(Ui.White, 0.08),
        };
        var close = Ui.Plain(cross, _close, "Close");
        close.VerticalAlignment = VerticalAlignment.Top;
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(title, 1);
        Grid.SetColumn(close, 2);
        head.Children.Add(swatch);
        head.Children.Add(title);
        head.Children.Add(close);

        var body = new StackPanel { Spacing = 9 };
        body.Children.Add(head);
        body.Children.Add(Ui.Text($"{status} · {_agents.Elapsed(agent)} · {agent.SessionName}", 11, Ui.Regular, 1, quiet));
        body.Children.Add(Section("Now", Line(agent.WorkLabel, ink, Ui.Medium)));
        if (calls.Count > 0)
        {
            body.Children.Add(Section("Before that", [.. calls.Select(call => Line(call, quiet, Ui.Regular))]));
        }
        if (agent.EditedFiles.Count > 0)
        {
            var count = agent.EditedFiles.Count;
            var files = Ui.MonoText(Files(agent), 11, Ui.Regular, 1, quiet);
            files.TextWrapping = TextWrapping.Wrap;
            files.MaxLines = 2;
            body.Children.Add(Section(count == 1 ? "Wrote 1 file" : $"Wrote {count} files", files));
        }
        _card.Child = body;
    }

    /// <summary>One line in the monospaced face, cut in its middle when too long.</summary>
    private static TextBlock Line(string text, Rgba colour, Windows.UI.Text.FontWeight weight)
    {
        var line = Ui.MonoText(text, 11, weight, 1, colour);
        Ui.Tooltip(line, text);
        return line;
    }

    private static StackPanel Section(string title, params UIElement[] content)
    {
        var section = new StackPanel { Spacing = 2 };
        section.Children.Add(Ui.Text(title, 11, Ui.Medium, 1, RoomSummary.Quiet));
        foreach (var element in content)
        {
            section.Children.Add(element);
        }
        return section;
    }

    /// <summary>Where it stands, in a word or two. While it works, what it works on is said under "Now".</summary>
    private string Status(RunningAgent agent)
    {
        switch (agent.Status(_agents.Now))
        {
            case AgentStatus.Working:
                return "Working";
            case AgentStatus.Idle:
                return "Waiting";
            default:
                var state = _agents.State(agent);
                return state.Length == 0 ? state : char.ToUpperInvariant(state[0]) + state[1..];
        }
    }

    /// <summary>The calls before the last one, newest first: "tests · swift test".</summary>
    private static List<string> Calls(RunningAgent agent) =>
        [.. agent.Recent.SkipLast(1).TakeLast(CallCount).Reverse()
             .Select(action => string.Join(" · ", new[] { action.Work.Label(), action.Detail ?? action.File }.OfType<string>()))];

    private static string Files(RunningAgent agent)
    {
        var shown = string.Join(", ", agent.EditedFiles.TakeLast(FileCount));
        var hidden = agent.EditedFiles.Count - FileCount;
        return hidden > 0 ? $"{shown} and {hidden} more" : shown;
    }

    /// <summary>
    /// Where the card goes for an agent at <paramref name="place"/> (the top left of its desk, in
    /// pixels): beside the desk, on the side of the room's middle, and never past the room's edges.
    /// <paramref name="height"/> is the card's own once laid out: Nunito's lines are taller than
    /// SF's, and a card with many files can pass Claudy's 200.
    /// </summary>
    public static Point Origin(Point place, Size room, double height = CardHeight)
    {
        double deskWidth = RoomGeometry.DeskSize.Width * RoomCanvas.Unit;
        var x = place.X < room.Width / 2 ? place.X + deskWidth + 8 : place.X - CardWidth - 8;
        var y = Math.Min(Math.Max(8, place.Y - 12), room.Height - Math.Max(height, CardHeight) - 8);
        return new Point(Math.Min(Math.Max(8, x), room.Width - CardWidth - 8), y);
    }
}
