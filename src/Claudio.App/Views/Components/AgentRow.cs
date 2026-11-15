using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Claudio.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace Claudio.App.Views;

/// <summary>
/// One agent in the card's Sessions tab, under its session, on two lines, as Claudy's
/// <c>AgentRow</c>: its Claudy, what it was asked and for how long; then its track over the last
/// ten minutes and where it is. A click unfolds it: what it wrote and its last calls. Its
/// session's row holds the way to stop it. Kept across scans and updated in place.
/// </summary>
internal sealed partial class AgentRow : Grid
{
    /// <summary>The row's Claudy, and the indent of the details under it.</summary>
    public const double ClaudyHeight = 27;
    public const double Spacing = 8;

    private readonly AgentsViewModel _model;
    private readonly AgentClaudy _claudy;
    private readonly TextBlock _title = Ui.Text(string.Empty, 11, Ui.Medium);
    private readonly TextBlock _elapsed = Ui.Text(string.Empty, 11, Ui.SemiBold, Ui.SecondaryOpacity);
    private readonly AgentTrack _track = new();
    private readonly AgentState _state = new(10, 110);
    private readonly AgentDetails _details = new();
    private readonly Border _background = new() { CornerRadius = new CornerRadius(8) };
    private RunningAgent? _agent;
    private bool _isExpanded;
    private bool _isHovered;

    public AgentRow(AgentsViewModel model)
    {
        _model = model;
        _claudy = new AgentClaudy(model, ClaudyHeight);
        Margin = new Thickness(-6, 0, -6, 0);
        Children.Add(_background);

        var top = Ui.Spread(_title, _elapsed, minimumGap: 8);
        var bottom = new Grid { ColumnSpacing = 8 };
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bottom.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _track.Height = AgentTrack.TrackHeight;
        Grid.SetColumn(_state, 1);
        bottom.Children.Add(_track);
        bottom.Children.Add(_state);
        var lines = Ui.Column(3, top, bottom);
        lines.VerticalAlignment = VerticalAlignment.Center;

        var summary = new Grid { ColumnSpacing = Spacing, Padding = new Thickness(0, 5, 0, 5), Background = Ui.Primary(0) };
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        summary.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(lines, 1);
        summary.Children.Add(_claudy);
        summary.Children.Add(lines);
        Ui.OnTap(summary, () =>
        {
            if (_agent is { } agent)
            {
                _model.ToggleExpanded(agent);
            }
        });

        _details.Margin = new Thickness((ClaudyHeight * MascotView.AspectRatio) + Spacing, 2, 0, 6);
        var column = Ui.Column(0, summary, _details);
        column.Padding = new Thickness(6, 0, 6, 0);
        Children.Add(column);
        PointerEntered += (_, _) =>
        {
            _isHovered = true;
            Paint();
        };
        PointerExited += (_, _) =>
        {
            _isHovered = false;
            Paint();
        };
    }

    public void Update(RunningAgent agent, bool isExpanded)
    {
        _agent = agent;
        _isExpanded = isExpanded;
        var now = _model.Now;
        var status = agent.Status(now);
        _claudy.Update(agent, status);
        Ui.Restyle(_title, agent.Title, status == AgentStatus.Done ? 0.55 : 0.86);
        Ui.Restyle(_elapsed, _model.Elapsed(agent), Ui.SecondaryOpacity);
        _track.Update(agent.Track(now));
        _state.Update(_model, agent, status);
        _details.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (isExpanded)
        {
            _details.Update(_model, agent);
        }
        Ui.Tooltip(this, isExpanded ? "Click to fold" : _model.Details(agent));
        Ui.Name(this, AgentClaudy.Spoken(agent, status));
        Paint();
    }

    private void Paint() => _background.Background = Ui.Primary(_isExpanded ? 0.06 : _isHovered ? 0.035 : 0);
}

/// <summary>What a row unfolds to, as Claudy's <c>AgentDetails</c>: how much it did, what it wrote, its last calls.</summary>
internal sealed partial class AgentDetails : StackPanel
{
    private readonly TextBlock _count = Line();
    private readonly TextBlock _wrote = Line();
    private readonly TextBlock _calls = Line();

    public AgentDetails()
    {
        Spacing = 5;
        _wrote.MaxLines = 2;
        _calls.TextWrapping = TextWrapping.NoWrap;
        _calls.TextTrimming = TextTrimming.CharacterEllipsis;
        Children.Add(_count);
        Children.Add(_wrote);
        Children.Add(_calls);
    }

    private static TextBlock Line()
    {
        var line = Ui.Text(string.Empty, 11, Ui.Medium, 0.55);
        line.TextWrapping = TextWrapping.Wrap;
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        return line;
    }

    public void Update(AgentsViewModel model, RunningAgent agent)
    {
        Ui.Restyle(_count, $"{agent.ActionCount} actions · started {UsageFormat.Clock(agent.StartedAt)}", 0.55);
        _wrote.Visibility = agent.EditedFiles.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Ui.Restyle(_wrote, "Wrote " + string.Join(", ", agent.EditedFiles), 0.9, Theme.Color(Accent.Coral));
        var calls = model.LastCalls(agent);
        _calls.Visibility = calls.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Ui.Restyle(_calls, "Last: " + string.Join(" · ", calls), 0.55);
    }
}

/// <summary>
/// An agent's own Claudy, as Claudy's <c>AgentClaudy</c>: its own colour, typing while it works,
/// grey and still once it falls silent, faded when it is done.
/// </summary>
internal sealed partial class AgentClaudy : Grid
{
    private readonly AgentsViewModel _model;
    private readonly MascotView _mascot;

    public AgentClaudy(AgentsViewModel model, double height)
    {
        _model = model;
        _mascot = new MascotView { Width = height * MascotView.AspectRatio, Height = height };
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_mascot);
    }

    public void Update(RunningAgent agent, AgentStatus status)
    {
        _mascot.Tint = _model.Tint(agent);
        _mascot.IsTyping = status == AgentStatus.Working;
        Opacity = status == AgentStatus.Done ? 0.6 : 1;
    }

    /// <summary>What a screen reader reads for a row: the drawing says nothing to it.</summary>
    public static string Spoken(RunningAgent agent, AgentStatus status)
    {
        var state = status switch
        {
            AgentStatus.Working => $"working on {agent.Touching}",
            AgentStatus.Idle => "waiting",
            AgentStatus.Stalled => "silent for a while",
            _ => "done",
        };
        return $"{agent.Title}, {agent.SessionName}, {state}";
    }
}

/// <summary>
/// Where an agent is, on the right of its row, as Claudy's <c>AgentState</c>: the file it touches,
/// in coral while it writes it; its silence in amber once it is worth a look; "done" and what it
/// wrote, in sage.
/// </summary>
internal sealed partial class AgentState : Grid
{
    private readonly TextBlock _text;

    public AgentState(double size, double maxWidth)
    {
        _text = Ui.Text(string.Empty, size, Ui.SemiBold);
        _text.TextTrimming = TextTrimming.CharacterEllipsis;
        _text.TextAlignment = TextAlignment.Right;
        // Its own width up to the cap, never more: the title takes what is left.
        MaxWidth = maxWidth;
        HorizontalAlignment = HorizontalAlignment.Right;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_text);
    }

    public void Update(AgentsViewModel model, RunningAgent agent, AgentStatus status)
    {
        var text = model.State(agent);
        switch (status)
        {
            case AgentStatus.Done:
                Ui.Restyle(_text, text, 1, Theme.Color(Accent.Sage));
                break;
            case AgentStatus.Stalled:
                Ui.Restyle(_text, text, 1, Theme.Color(Accent.Amber));
                break;
            case AgentStatus.Working when agent.IsWriting:
                Ui.Restyle(_text, text, 1, Theme.Color(Accent.Coral));
                break;
            default:
                Ui.Restyle(_text, text, 0.42);
                break;
        }
        Ui.Tooltip(this, text);
    }
}

/// <summary>What the tracks mean, in the section header, as Claudy's <c>AgentTrackLegend</c>: their reach, and the colour of a write.</summary>
internal sealed partial class AgentTrackLegend : StackPanel
{
    public AgentTrackLegend()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 6;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(new Rectangle { Width = 2, Height = 10, RadiusX = 1, RadiusY = 1, Fill = Ui.Brush(Theme.Color(Accent.Coral)), VerticalAlignment = VerticalAlignment.Center });
        Children.Add(Ui.Meta("Edits, last 10 min"));
        Ui.Name(this, "Tracks cover the last ten minutes; writes are drawn in coral");
    }
}

/// <summary>
/// An agent's last ten minutes, one bar per fifteen seconds, as Claudy's <c>AgentTrack</c>: a write
/// in coral and tall, any other call a short grey tick. A burst of edits stands out; a flat track
/// is a silent agent.
/// </summary>
internal sealed partial class AgentTrack : Canvas
{
    public const double TrackHeight = 14;

    /// <summary>Share of a slot the bar fills; the rest is the gap to the next one.</summary>
    private const double BarShare = 0.62;
    private const double EditBase = 6;
    private const double EditStep = 4;
    private const double OtherBase = 2;
    private const double OtherStep = 1.5;
    private const double OtherCap = 7;

    private IReadOnlyList<AgentTrackSlot> _slots = [];

    public AgentTrack()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        SizeChanged += (_, _) => Draw();
    }

    public void Update(IReadOnlyList<AgentTrackSlot> slots)
    {
        _slots = slots;
        Draw();
    }

    private void Draw()
    {
        Children.Clear();
        var width = ActualWidth;
        var height = ActualHeight > 0 ? ActualHeight : TrackHeight;
        if (width <= 0)
        {
            return;
        }
        Add(0, height - 1, width, 1, Ui.Primary(0.1));
        if (_slots.Count == 0)
        {
            return;
        }
        var step = width / _slots.Count;
        var bar = Math.Max(1.2, step * BarShare);
        for (var index = 0; index < _slots.Count; index++)
        {
            var slot = _slots[index];
            var x = (index * step) + ((step - bar) / 2);
            if (slot.Edits > 0)
            {
                var tall = Math.Min(height, EditBase + (slot.Edits * EditStep));
                Add(x, height - tall, bar, tall, Ui.Brush(Theme.Color(Accent.Coral)));
            }
            else if (slot.Others > 0)
            {
                var tall = Math.Min(OtherCap, OtherBase + (slot.Others * OtherStep));
                Add(x, height - tall, bar, tall, Ui.Primary(0.32));
            }
        }
    }

    private void Add(double x, double y, double width, double height, Microsoft.UI.Xaml.Media.Brush fill)
    {
        var rectangle = new Rectangle { Width = width, Height = height, Fill = fill };
        SetLeft(rectangle, x);
        SetTop(rectangle, y);
        Children.Add(rectangle);
    }
}
