using Claudio.App.Views;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App;

/// <summary>
/// What Claudy's second screen opens, as Claudy's <c>RoomSummary</c>: the session in plain
/// sentences. What was asked, what happens now, how much of the limit is used, and the two things
/// to do about it, as the Sessions tab offers them.
/// </summary>
internal sealed partial class RoomSummary : Grid
{
    /// <summary>
    /// The room is dark at any hour, so what lies on it is lettered in fixed light inks: the
    /// system's own would turn dark with a light theme.
    /// </summary>
    public static Rgba Ink { get; } = Rgba.Parse("#f4ede4");

    public static Rgba Quiet { get; } = Rgba.Parse("#b9aea3");

    /// <summary>The surface of the cards laid on the room: dark clay, a light hairline.</summary>
    public static void Surface(Border card)
    {
        card.Background = Ui.Brush(Rgba.Parse("#1c1714"), 0.96);
        card.BorderBrush = Ui.Brush(Ui.White, 0.14);
        card.BorderThickness = new Thickness(1);
        card.CornerRadius = new CornerRadius(Theme.Metric("tileCorner"));
    }

    private readonly AgentsViewModel _agents;
    private readonly UsageViewModel _usage;
    private readonly Border _card = new() { Padding = new Thickness(Theme.Metric("tilePadding")) };
    private string? _key;

    public RoomSummary(AgentsViewModel agents, UsageViewModel usage)
    {
        _agents = agents;
        _usage = usage;
        Surface(_card);
        Children.Add(_card);
    }

    public void Update(RoomViewModel.Project project)
    {
        var lead = project.Lead;
        var isConfirming = lead is not null && _agents.InterruptPending == lead.Id;
        var tally = new RoomTally(project, _agents.Now);
        var sentences = RoomSummaryText.Sentences(tally, lead);
        var snapshot = _usage.Snapshot;
        var key = string.Join('|', sentences.Select(line => line.Text))
                  + $"|{lead?.LastPrompt}|{isConfirming}|{lead?.Host?.Name}|{lead?.CanBeInterrupted}|{lead?.Name}"
                  + $"|{snapshot.Session.IsMeasured}:{snapshot.Session.Percent:0.00}:{snapshot.Session.ResetDate}|{snapshot.Weekly.IsMeasured}:{snapshot.Weekly.Percent:0.00}";
        if (key == _key)
        {
            return;
        }
        _key = key;

        var columns = new Grid { ColumnSpacing = 16 };
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Spacing = 10 };
        if (Asked(lead) is { } asked)
        {
            left.Children.Add(asked);
        }
        left.Children.Add(RightNow(sentences));
        columns.Children.Add(left);
        var limit = Limit(snapshot);
        Grid.SetColumn(limit, 1);
        columns.Children.Add(limit);

        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(columns);
        if (lead is not null)
        {
            if (isConfirming)
            {
                body.Children.Add(Confirmation(lead));
            }
            else if (lead.Host is not null || lead.CanBeInterrupted)
            {
                body.Children.Add(Actions(lead));
            }
        }
        _card.Child = body;
    }

    private static TextBlock Label(string text) => Ui.Text(text, 11, Ui.Medium, 1, Quiet);

    private static TextBlock Wrapping(TextBlock block, int lines = 0)
    {
        block.TextWrapping = TextWrapping.Wrap;
        block.MaxLines = lines;
        return block;
    }

    // What was asked

    private static StackPanel? Asked(ClaudeSession? lead)
    {
        if (lead?.LastPrompt is not { } prompt)
        {
            return null;
        }
        var text = Wrapping(Ui.Text(prompt, 13, Ui.SemiBold, 1, Ink), lines: 3);
        Ui.Tooltip(text, prompt);
        return Ui.Column(3, Label("You asked"), text);
    }

    // What happens now

    private static StackPanel RightNow(IReadOnlyList<(string Text, Ink? Ink)> sentences)
    {
        var column = new StackPanel { Spacing = 6 };
        column.Children.Add(Label("Right now"));
        foreach (var (text, ink) in sentences)
        {
            var colour = ink is { } accent ? Theme.Color(accent, isDark: true) : (Rgba?)null;
            var dot = Ui.Dot(7, colour ?? Theme.Idle);
            dot.VerticalAlignment = VerticalAlignment.Top;
            dot.Margin = new Thickness(0, 5, 0, 0);
            var line = new Grid { ColumnSpacing = 7 };
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            line.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var words = Wrapping(Ui.Text(text, 12, Ui.Medium, 1, colour ?? Ink));
            Grid.SetColumn(words, 1);
            line.Children.Add(dot);
            line.Children.Add(words);
            column.Children.Add(line);
        }
        return column;
    }

    // The limit

    private static StackPanel Limit(UsageSnapshot snapshot)
    {
        var column = new StackPanel { Spacing = 6 };
        column.Children.Add(Label("Your limit"));
        column.Children.Add(Gauge("This session", snapshot.Session));
        var now = DateTimeOffset.UtcNow;
        if (snapshot.Session.IsActive(now))
        {
            column.Children.Add(Ui.Text($"Starts again at {UsageFormat.ResetTime(snapshot.Session.ResetDate, now)}", 11, Ui.Regular, 1, Quiet));
        }
        column.Children.Add(Gauge("This week", snapshot.Weekly));
        return column;
    }

    private static StackPanel Gauge(string title, UsageWindow window)
    {
        var used = window.IsMeasured
            ? $"{(int)Math.Round(window.Percent * 100, MidpointRounding.AwayFromZero)} % used"
            : "not measured";
        var bar = new UsageBar(5);
        bar.Update(window.IsMeasured ? window.Percent : 0, Theme.Tint(window.Accent, window.Percent), animated: false);
        return Ui.Column(4, Ui.Spread(Ui.Text(title, 12, Ui.Medium, 1, Ink), Ui.Text(used, 12, Ui.Medium, 1, Ink), minimumGap: 4), bar);
    }

    // Actions

    private StackPanel Actions(ClaudeSession session)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (session.Host is { } host)
        {
            var open = QuietButton.Make($"Open {host.ShortName}", () => _agents.Focus(session), Ink);
            Ui.Tooltip(open, $"Brings {host.Name} forward");
            row.Children.Add(open);
        }
        if (session.CanBeInterrupted)
        {
            var interrupt = QuietButton.Make("Interrupt", () => _agents.AskToInterrupt(session), Theme.Danger, RoomGlyphs.Stop);
            Ui.Tooltip(interrupt, "Sends Ctrl+C to this Claude Code session");
            row.Children.Add(interrupt);
        }
        return row;
    }

    private StackPanel Confirmation(ClaudeSession session)
    {
        var text = Wrapping(Ui.Text($"Sends Ctrl+C to {session.Name}: its turn and every agent of it stop. The session stays open.", 11, Ui.Medium, 1, Quiet));
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(QuietButton.Make("Cancel", _agents.CancelInterrupt, Ink));
        // No Enter shortcut: the destructive choice takes a click.
        buttons.Children.Add(QuietButton.Make($"Interrupt {session.Name}", () => _agents.Interrupt(session), Theme.Danger));
        return Ui.Column(6, text, buttons);
    }
}
