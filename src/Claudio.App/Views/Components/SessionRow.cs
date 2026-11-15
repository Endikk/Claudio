using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Services;
using Claudio.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App.Views;

/// <summary>
/// One Claude Code session in the card's Sessions tab, as Claudy's <c>SessionRow</c>: its line, its
/// details once unfolded, and the agents it runs underneath. A session that waits for the user
/// takes the amber waiting row as its line, with the way to its window in reach.
/// </summary>
internal sealed partial class SessionRow : StackPanel
{
    /// <summary>The agents' rule sits under the session's dot, and their rows just right of it.</summary>
    public const double AgentIndent = 12;

    private readonly AgentsViewModel _model;
    private readonly WaitingRow _waiting;
    private readonly SessionHeader _header;
    private readonly SessionDetails _details;
    private readonly Grid _agents = new();
    private readonly StackPanel _agentList = new();
    private readonly Border _rule = new() { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(4, 0, 0, 0) };
    private readonly Dictionary<string, AgentRow> _rows = [];
    private string _shape = string.Empty;

    public SessionRow(AgentsViewModel model)
    {
        _model = model;
        _waiting = new WaitingRow(model, compact: true);
        _header = new SessionHeader(model);
        _details = new SessionDetails(model);
        _details.Margin = new Thickness(AgentIndent + 4, 4, 0, 6);
        Ui.OnTap(_waiting, () =>
        {
            if (_waiting.Session is { } session)
            {
                _model.ToggleExpanded(session);
            }
        });
        _agentList.Padding = new Thickness(AgentIndent, 0, 0, 0);
        _agents.Children.Add(_agentList);
        _agents.Children.Add(_rule);
        Children.Add(_waiting);
        Children.Add(_header);
        Children.Add(_details);
        Children.Add(_agents);
    }

    public void Update(SessionGroup group)
    {
        var session = group.Session;
        var isExpanded = _model.ExpandedSessionId == session.Id;
        _waiting.Visibility = session.IsWaiting ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = session.IsWaiting ? Visibility.Collapsed : Visibility.Visible;
        if (session.IsWaiting)
        {
            _waiting.Update(session);
        }
        else
        {
            _header.Update(session, isExpanded);
        }
        _details.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (isExpanded)
        {
            _details.Update(session, group.Agents.Count);
        }

        foreach (var gone in _rows.Keys.Except(group.Agents.Select(agent => agent.Id)).ToList())
        {
            _rows.Remove(gone);
        }
        foreach (var agent in group.Agents)
        {
            if (!_rows.TryGetValue(agent.Id, out var row))
            {
                row = new AgentRow(_model);
                _rows[agent.Id] = row;
            }
            row.Update(agent, _model.ExpandedId == agent.Id);
        }
        var shape = string.Join(',', group.Agents.Select(agent => agent.Id));
        if (shape != _shape)
        {
            _shape = shape;
            _agentList.Children.Clear();
            foreach (var agent in group.Agents)
            {
                _agentList.Children.Add(_rows[agent.Id]);
            }
        }
        _rule.Background = Ui.Primary(0.1);
        _agents.Visibility = group.Agents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>
/// A session's line, as Claudy's <c>SessionHeader</c>: its state as a dot, its name and project,
/// then what it does and how long it has been open. A click unfolds it.
/// </summary>
internal sealed partial class SessionHeader : Grid
{
    private readonly AgentsViewModel _model;
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse _dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(1, 0, 1, 0) };
    private readonly TextBlock _name = Ui.Text(string.Empty, 13, Ui.SemiBold);
    private readonly TextBlock _folder = Ui.Text(string.Empty, 11, Ui.Medium, Ui.SecondaryOpacity);
    private readonly TextBlock _state = Ui.Text(string.Empty, 11, Ui.SemiBold);
    private readonly TextBlock _age = Ui.Text(string.Empty, 11, Ui.SemiBold, Ui.SecondaryOpacity);
    private readonly FontIcon _chevron = Ui.Icon("\uE70D", 10, Ui.SecondaryOpacity);
    private readonly RotateTransform _turn = new() { CenterX = 5, CenterY = 5 };
    private readonly Border _background = new() { CornerRadius = new CornerRadius(8) };
    private ClaudeSession? _session;
    private bool _isExpanded;
    private bool _isHovered;

    public SessionHeader(AgentsViewModel model)
    {
        _model = model;
        Margin = new Thickness(-6, 0, -6, 0);
        _chevron.RenderTransform = _turn;
        _chevron.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { ColumnSpacing = 8, MinHeight = 22, Padding = new Thickness(6, 5, 6, 5) };
        // The dot, the name (a long one gives way before it pushes the row past the card's edge),
        // the folder, then the state, the age and the chevron at the far right.
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 170 });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        foreach (var (element, column) in new (FrameworkElement, int)[] { (_dot, 0), (_name, 1), (_folder, 2), (_state, 3), (_age, 4), (_chevron, 5) })
        {
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(element, column);
            row.Children.Add(element);
        }
        Children.Add(_background);
        Children.Add(row);
        Background = Ui.Primary(0);
        Ui.OnTap(this, () =>
        {
            if (_session is { } session)
            {
                _model.ToggleExpanded(session);
            }
        });
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

    public void Update(ClaudeSession session, bool isExpanded)
    {
        _session = session;
        _isExpanded = isExpanded;
        _dot.Fill = Ui.Brush(session.Status switch
        {
            SessionState.Busy => Theme.Color(Accent.Coral),
            SessionState.Waiting => Theme.Color(Accent.Amber),
            _ => Theme.Idle,
        });
        Ui.Restyle(_name, session.Name, 1);
        var folder = session.FolderName is { } name && name != session.Name ? name : null;
        Ui.Restyle(_folder, folder ?? string.Empty, Ui.SecondaryOpacity);
        var state = SentenceCased(_model.State(session));
        if (session.Status == SessionState.Busy)
        {
            Ui.Restyle(_state, state, 1, Ui.Ink(Ink.Coral));
        }
        else
        {
            Ui.Restyle(_state, state, Ui.SecondaryOpacity);
        }
        var age = _model.Age(session);
        Ui.Restyle(_age, age ?? string.Empty, Ui.SecondaryOpacity);
        _age.Visibility = age is null ? Visibility.Collapsed : Visibility.Visible;
        _chevron.Foreground = Ui.Secondary;
        _turn.Angle = isExpanded ? 0 : -90;
        Ui.Tooltip(this, session.LastPrompt is { } prompt ? $"Last: {prompt}" : isExpanded ? "Click to fold" : "Click to unfold");
        Ui.Name(this, $"{session.Name}, {session.FolderName ?? string.Empty}, {_model.State(session)}");
        Paint();
    }

    private void Paint() => _background.Background = Ui.Primary(_isExpanded ? 0.06 : _isHovered ? 0.035 : 0);

    /// <summary>"working" as a status beside a name reads "Working".</summary>
    public static string SentenceCased(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
}

/// <summary>
/// What a session unfolds to, as Claudy's <c>SessionDetails</c>: where it runs, the last thing
/// asked of it, the way to its window, and the interrupt. The interrupt is two clicks: the first
/// one asks, naming the session and saying what the signal does, since it reaches every agent of
/// that session.
/// </summary>
internal sealed partial class SessionDetails : StackPanel
{
    private readonly AgentsViewModel _model;
    private readonly TextBlock _details = Line();
    private readonly TextBlock _prompt = Line();
    private readonly ContentControl _actions = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private string? _actionsKey;

    public SessionDetails(AgentsViewModel model)
    {
        _model = model;
        Spacing = 5;
        _prompt.TextWrapping = TextWrapping.NoWrap;
        _prompt.TextTrimming = TextTrimming.CharacterEllipsis;
        _actions.Margin = new Thickness(0, 2, 0, 0);
        Children.Add(_details);
        Children.Add(_prompt);
        Children.Add(_actions);
    }

    private static TextBlock Line()
    {
        var line = Ui.Text(string.Empty, 11, Ui.Medium, Ui.SecondaryOpacity);
        line.TextWrapping = TextWrapping.Wrap;
        line.TextTrimming = TextTrimming.CharacterEllipsis;
        return line;
    }

    public void Update(ClaudeSession session, int agentCount)
    {
        Ui.Restyle(_details, _model.Details(session, agentCount), Ui.SecondaryOpacity);
        _prompt.Visibility = session.LastPrompt is null ? Visibility.Collapsed : Visibility.Visible;
        if (session.LastPrompt is { } prompt)
        {
            Ui.Restyle(_prompt, $"Last: “{prompt}”", Ui.SecondaryOpacity);
            Ui.Tooltip(_prompt, prompt);
        }

        var isConfirming = _model.InterruptPending == session.Id;
        var key = $"{session.Id}|{isConfirming}|{session.Host?.Name}|{session.CanBeInterrupted}|{Ui.IsDark}";
        if (key == _actionsKey)
        {
            return;
        }
        _actionsKey = key;
        if (isConfirming)
        {
            _actions.Content = Confirmation(session);
        }
        else if (session.Host is not null || session.CanBeInterrupted)
        {
            _actions.Content = Actions(session);
        }
        else
        {
            _actions.Content = null;
        }
    }

    private StackPanel Actions(ClaudeSession session)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        if (session.Host is { } host)
        {
            var open = QuietButton.Make($"Open {host.ShortName}", () => _model.Focus(session));
            Ui.Tooltip(open, $"Brings {host.Name} forward");
            row.Children.Add(open);
        }
        if (session.CanBeInterrupted)
        {
            var interrupt = QuietButton.Make("Interrupt", () => _model.AskToInterrupt(session), Theme.Danger, "\uE71A");
            Ui.Tooltip(interrupt, "Sends Ctrl+C to this Claude Code session");
            row.Children.Add(interrupt);
        }
        return row;
    }

    private StackPanel Confirmation(ClaudeSession session)
    {
        var text = Ui.Text($"Sends Ctrl+C to {session.Name}: its turn and every agent of it stop. The session stays open.", 11, Ui.Medium, Ui.SecondaryOpacity);
        text.TextWrapping = TextWrapping.Wrap;
        text.MaxLines = 3;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = QuietButton.Make("Cancel", _model.CancelInterrupt);
        // Escape cancels; no Enter shortcut: the destructive choice takes a click.
        cancel.KeyboardAccelerators.Add(new Microsoft.UI.Xaml.Input.KeyboardAccelerator { Key = Windows.System.VirtualKey.Escape });
        buttons.Children.Add(cancel);
        buttons.Children.Add(QuietButton.Make($"Interrupt {session.Name}", () => _model.Interrupt(session), Theme.Danger));
        return Ui.Column(6, text, buttons);
    }
}

/// <summary>A small text button on the card's glass, as Claudy's <c>QuietButtonStyle</c>: a capsule its tint washes, deeper when pressed.</summary>
internal static class QuietButton
{
    public static Button Make(string title, Action action, Rgba? tint = null, string? glyph = null) =>
        Ui.PillButton(title, action, tint ?? Ui.PrimaryInk with { A = (byte)Math.Round(255 * Ui.SecondaryOpacity) }, isProminent: false, glyph: glyph);
}

/// <summary>
/// A session waiting on the user, in amber, as Claudy's <c>WaitingRow</c>: who waits, for how long,
/// for what, and the way to its window. In the card it heads its session; in the island and the
/// flyout it comes first.
/// </summary>
internal sealed partial class WaitingRow : Grid
{
    private readonly AgentsViewModel _model;
    private readonly bool _compact;
    private readonly Border _tile;
    private readonly ContentControl _hand = new() { IsTabStop = false, Width = 20, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = Ui.Text(string.Empty, 13, Ui.SemiBold);
    private readonly TextBlock _detail = Ui.Text(string.Empty, 11, Ui.Regular, Ui.SecondaryOpacity);
    private readonly ContentControl _open = new() { IsTabStop = false, VerticalAlignment = VerticalAlignment.Center };
    private string? _openKey;

    /// <param name="compact">The card's width keeps the button to "Open"; the island has room for the app's name.</param>
    public WaitingRow(AgentsViewModel model, bool compact = false)
    {
        _model = model;
        _compact = compact;
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = Ui.Column(2, _title, _detail);
        text.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(text, 1);
        Grid.SetColumn(_open, 2);
        content.Children.Add(_hand);
        content.Children.Add(text);
        content.Children.Add(_open);
        _tile = Ui.Tile(content, Theme.Color(Accent.Amber), 0.3, 12);
        Children.Add(_tile);
    }

    public ClaudeSession? Session { get; private set; }

    public void Update(ClaudeSession session)
    {
        Session = session;
        Ui.RestyleTile(_tile, Theme.Color(Accent.Amber), 0.3);
        _hand.Content = Ui.Hand(15, Ui.Ink(Ink.Amber));
        Ui.Restyle(_title, $"{session.Name} is waiting for you", 1);
        // The wait leads the second line: the title never has to give way to it.
        _detail.Inlines.Clear();
        _detail.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = _model.Waited(session), Foreground = Ui.Primary(1) });
        _detail.Inlines.Add(new Microsoft.UI.Xaml.Documents.Run { Text = " · " + _model.WaitingDetail(session), Foreground = Ui.Secondary });
        var key = $"{session.Id}|{session.Host?.Name}|{Ui.IsDark}";
        if (key != _openKey)
        {
            _openKey = key;
            if (session.Host is { } host)
            {
                var open = Ui.PillButton(_compact ? "Open" : $"Open {host.ShortName}", () => _model.Focus(session), Theme.Color(Accent.Amber));
                Ui.Tooltip(open, $"Brings {host.Name} forward");
                _open.Content = open;
            }
            else
            {
                _open.Content = null;
            }
        }
        Ui.Name(this, $"{session.Name} is waiting for you: {_model.WaitingDetail(session)}");
    }
}

/// <summary>
/// The sessions waiting on the user, for the island and the flyout, as Claudy's <c>WaitingList</c>:
/// each its own amber row, above the quotas, since an answer is what unblocks the work.
/// </summary>
internal sealed partial class WaitingList : StackPanel
{
    private readonly AgentsViewModel _model;
    private readonly bool _compact;
    private readonly Dictionary<string, WaitingRow> _rows = [];
    private string _shape = string.Empty;

    public WaitingList(AgentsViewModel model, bool compact = false)
    {
        _model = model;
        _compact = compact;
        Spacing = Theme.Metric("gap");
    }

    public void Update()
    {
        var waiting = _model.Waiting;
        foreach (var gone in _rows.Keys.Except(waiting.Select(session => session.Id)).ToList())
        {
            _rows.Remove(gone);
        }
        foreach (var session in waiting)
        {
            if (!_rows.TryGetValue(session.Id, out var row))
            {
                row = new WaitingRow(_model, _compact);
                _rows[session.Id] = row;
            }
            row.Update(session);
        }
        var shape = string.Join(',', waiting.Select(session => session.Id));
        if (shape != _shape)
        {
            _shape = shape;
            Children.Clear();
            foreach (var session in waiting)
            {
                Children.Add(_rows[session.Id]);
            }
        }
        Visibility = waiting.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
