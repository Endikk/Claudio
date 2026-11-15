using System.Numerics;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App;

/// <summary>
/// The agent room's window, as Claudy's <c>RoomView</c>: the card's glass around the open space of
/// one project. On top, the projects with a session open; underneath, the session's line as the
/// Sessions tab writes it. <see cref="Update"/> follows the room's model.
/// </summary>
internal sealed partial class RoomView : Grid
{
    private readonly RoomViewModel _room;
    private readonly AgentsViewModel _agents;
    private readonly UsageViewModel _usage;
    private readonly RoomCanvas _canvas = new();
    private readonly RoomOverview _overview;
    private readonly Grid _stage = new() { Width = RoomCanvas.Size.Width, Height = RoomCanvas.Size.Height };
    private readonly Canvas _overlay = new() { Width = RoomCanvas.Size.Width, Height = RoomCanvas.Size.Height };
    private readonly RoomSwitcher _switcher;
    private readonly ContentControl _footer = new() { IsTabStop = false, HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Center };
    private RoomSummary? _summary;
    private RoomAgentCard? _card;
    private string? _footerKey;
    private bool? _showsOverview;

    public RoomView(RoomViewModel room, AgentsViewModel agents, UsageViewModel usage, Action close)
    {
        _room = room;
        _agents = agents;
        _usage = usage;
        _overview = new RoomOverview(room, agents, usage);
        _switcher = new RoomSwitcher(room);
        var gap = Theme.Metric("gap");
        Padding = new Thickness(Theme.Metric("panelPadding"));
        RowSpacing = gap;
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        // Header: the title, the projects, the room's menu and the close button.
        var header = new Grid { Height = 44, Padding = new Thickness(6, 0, 6, 0), ColumnSpacing = 12 };
        foreach (var width in new[] { GridLength.Auto, GridLength.Auto, new GridLength(1, GridUnitType.Star), GridLength.Auto, GridLength.Auto })
        {
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
        }
        var title = Ui.Text("Agents", 15, Ui.SemiBold);
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        Grid.SetColumn(_switcher, 1);
        header.Children.Add(_switcher);
        var (menu, _) = Ui.RoundButton(RoomGlyphs.Ellipsis, () => { }, "The weather in the window");
        menu.Click += (_, _) => Menu().ShowAt(menu);
        Ui.Name(menu, "Room options");
        Grid.SetColumn(menu, 3);
        header.Children.Add(menu);
        var (shut, _) = Ui.RoundButton(RoomGlyphs.Close, close, "Close the room");
        Ui.Name(shut, "Close");
        Grid.SetColumn(shut, 4);
        header.Children.Add(shut);
        Children.Add(header);

        // The room, cut to the tile's corner, the summary and an agent's card over it.
        _stage.Loaded += (_, _) => Round(_stage, Theme.Metric("tileCorner"));
        _canvas.Scene = (date, withHelp) => _room.Scene(_room.Selected, date, Quota, withHelp);
        _canvas.DeskTapped += desk =>
        {
            if (_room.Scene(_room.Selected, DateTimeOffset.UtcNow, Quota, withHelp: false).Desks[desk] is { } occupant)
            {
                _room.ToggleAgent(occupant.Id);
            }
        };
        _canvas.LoungerTapped += _room.ToggleAgent;
        _canvas.SummaryTapped += () =>
        {
            _room.IsSummaryOpen = !_room.IsSummaryOpen;
            if (_room.IsSummaryOpen)
            {
                _room.SelectedAgentId = null;
            }
        };
        Grid.SetRow(_stage, 1);
        Children.Add(_stage);

        _footer.Height = 36;
        _footer.Padding = new Thickness(8, 0, 8, 0);
        Grid.SetRow(_footer, 2);
        Children.Add(_footer);
    }

    /// <summary>The session quota, 0…1, while it is measured.</summary>
    private double? Quota => _usage.Snapshot.Session is { IsMeasured: true } session ? session.Percent : null;

    /// <summary>Every project at once, when there is more than one to show.</summary>
    private bool ShowsOverview => _room.IsOverview && _room.Projects.Count > 1;

    /// <summary>Reads the room's model again: the projects, the drawing's inputs, the cards over it, the footer.</summary>
    public void Update()
    {
        _switcher.Update();
        var overview = ShowsOverview;
        if (overview != _showsOverview)
        {
            _showsOverview = overview;
            // What is not on screen is taken out, so that it draws nothing.
            _stage.Children.Clear();
            if (overview)
            {
                _stage.Children.Add(_overview);
            }
            else
            {
                _stage.Children.Add(_canvas);
                _stage.Children.Add(_overlay);
            }
        }
        if (overview)
        {
            _overview.Update();
        }
        else
        {
            _canvas.Title = _room.Selected?.Id ?? RoomPainter.ChiefName;
            _canvas.Week = Week();
            _canvas.IsSummaryOpen = _room.IsSummaryOpen;
            _canvas.WeatherChoice = _room.WeatherChoice;
            UpdateSummary();
            UpdateCard();
        }
        UpdateFooter(overview);
    }

    /// <summary>The tokens of the last seven days, each as a share of the busiest: the board's bars.</summary>
    private List<double> Week()
    {
        var days = _usage.Snapshot.History.TakeLast(7).ToList();
        var most = days.Count == 0 ? 0 : days.Max(day => day.Tokens);
        return most > 0 ? [.. days.Select(day => (double)day.Tokens / most)] : [];
    }

    /// <summary>The weather in the window, and the rooms put away. The hour is the PC's own.</summary>
    private MenuFlyout Menu()
    {
        var flyout = new MenuFlyout();
        flyout.Items.Add(new MenuFlyoutItem { Text = "Weather", IsEnabled = false });
        var choices = new List<(string Title, string Value)> { ("As it comes", RoomWeathers.AsItComes) };
        choices.AddRange(RoomWeathers.AllCases.Select(weather => (weather.Title(), weather.Id())));
        var chosen = RoomWeathers.AllCases.Any(weather => weather.Id() == _room.WeatherChoice) ? _room.WeatherChoice : RoomWeathers.AsItComes;
        foreach (var (title, value) in choices)
        {
            var item = new RadioMenuFlyoutItem { Text = title, GroupName = "weather", IsChecked = value == chosen };
            item.Click += (_, _) =>
            {
                _room.WeatherChoice = value;
                Update();
            };
            flyout.Items.Add(item);
        }
        if (_room.Hidden.Count > 0)
        {
            flyout.Items.Add(new MenuFlyoutSeparator());
            flyout.Items.Add(new MenuFlyoutItem { Text = "Rooms put away", IsEnabled = false });
            foreach (var name in _room.Hidden)
            {
                var item = new MenuFlyoutItem { Text = $"Show {name}" };
                item.Click += (_, _) => _room.Restore(name);
                flyout.Items.Add(item);
            }
        }
        return flyout;
    }

    // Footer

    private void UpdateFooter(bool overview)
    {
        var project = _room.Selected;
        var lead = project?.Lead;
        var tally = project is null ? default : new RoomTally(project, _agents.Now);
        var key = overview ? $"all|{_room.Projects.Count}|{Ui.IsDark}"
            : lead is null ? $"none|{Ui.IsDark}"
            : $"{project!.Id}|{lead.Name}|{lead.Status}|{tally}|{_agents.Age(lead)}|{Ui.IsDark}";
        if (key == _footerKey)
        {
            return;
        }
        _footerKey = key;
        var row = new Grid { ColumnSpacing = 8 };
        void Add(FrameworkElement element, GridLength width)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = width });
            element.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(element, row.ColumnDefinitions.Count - 1);
            row.Children.Add(element);
        }
        var fill = new GridLength(1, GridUnitType.Star);
        if (overview)
        {
            var count = _room.Projects.Count;
            Add(Ui.Text(count == 1 ? "1 project open" : $"{count} projects open", 12, Ui.Medium, Ui.SecondaryOpacity), fill);
        }
        else if (project is not null && lead is not null)
        {
            Add(Ui.Dot(7, Dot(lead)), GridLength.Auto);
            Add(Ui.Text(lead.Name, 13, Ui.SemiBold), GridLength.Auto);
            if (lead.Name != project.Id)
            {
                Add(Ui.Text(project.Id, 11, Ui.Medium, Ui.SecondaryOpacity), GridLength.Auto);
            }
            Add(new Border(), fill);
            if (tally.Working > 0)
            {
                Add(RoomBadge.Make($"{tally.Working} working", Ink.Coral), GridLength.Auto);
            }
            if (tally.Waiting > 0)
            {
                Add(RoomBadge.Make($"{tally.Waiting} waiting", Ink.Amber), GridLength.Auto);
            }
            if (tally.Stuck > 0)
            {
                Add(RoomBadge.Make($"{tally.Stuck} stuck", Ink.Danger), GridLength.Auto);
            }
            if (tally.Done > 0)
            {
                Add(RoomBadge.Make($"{tally.Done} done", Ink.Positive), GridLength.Auto);
            }
            if (_agents.Age(lead) is { } age)
            {
                Add(Ui.Text(age, 11, Ui.SemiBold, Ui.SecondaryOpacity), GridLength.Auto);
            }
        }
        else
        {
            Add(Ui.Text("No Claude Code session open", 12, Ui.Medium, Ui.SecondaryOpacity), fill);
        }
        _footer.Content = row;
    }

    private static Rgba Dot(ClaudeSession session) => session.Status switch
    {
        SessionState.Busy => Theme.Color(Claudio.Core.Services.Accent.Coral),
        SessionState.Waiting => Theme.Color(Claudio.Core.Services.Accent.Amber),
        _ => Theme.Idle,
    };

    // The cards over the room

    /// <summary>Under Claudy's desk, between the two lower areas.</summary>
    private void UpdateSummary()
    {
        if (!_room.IsSummaryOpen || _room.Selected is not { } project)
        {
            if (_summary is not null)
            {
                _overlay.Children.Remove(_summary);
                _summary = null;
            }
            return;
        }
        if (_summary is null)
        {
            _summary = new RoomSummary(_agents, _usage) { Width = 360 };
            Canvas.SetLeft(_summary, 300);
            Canvas.SetTop(_summary, 322);
            _overlay.Children.Add(_summary);
            Appear(_summary, scales: true);
        }
        _summary.Update(project);
    }

    /// <summary>Beside the desk of the agent a click chose.</summary>
    private void UpdateCard()
    {
        if (_room.SelectedAgent is not { } agent || _room.Place(agent.Id) is not { } place)
        {
            if (_card is not null)
            {
                _overlay.Children.Remove(_card);
                _card = null;
            }
            return;
        }
        if (_card is null || _card.AgentId != agent.Id)
        {
            if (_card is not null)
            {
                _overlay.Children.Remove(_card);
            }
            _card = new RoomAgentCard(_agents, () => _room.SelectedAgentId = null);
            _card.SizeChanged += (_, _) => UpdateCard();
            _overlay.Children.Add(_card);
            Appear(_card, scales: false);
        }
        var origin = RoomAgentCard.Origin(new Windows.Foundation.Point(place.X * RoomCanvas.Unit, place.Y * RoomCanvas.Unit), RoomCanvas.Size,
                                          _card.ActualHeight > 0 ? _card.ActualHeight : RoomAgentCard.CardHeight);
        Canvas.SetLeft(_card, origin.X);
        Canvas.SetTop(_card, origin.Y);
        _card.Update(agent);
    }

    /// <summary>
    /// A card coming into view, as Claudy's popup transition: it fades in, the summary growing from
    /// a little smaller, under its top edge. Still under reduced motion.
    /// </summary>
    private static void Appear(FrameworkElement element, bool scales)
    {
        if (Motion.ReducesMotion)
        {
            return;
        }
        element.Loaded += Start;

        void Start(object sender, RoutedEventArgs args)
        {
            element.Loaded -= Start;
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            var duration = TimeSpan.FromMilliseconds(220);
            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, 0);
            fade.InsertKeyFrame(1, 1);
            fade.Duration = duration;
            visual.StartAnimation("Opacity", fade);
            if (scales)
            {
                visual.CenterPoint = new Vector3((float)(element.ActualWidth / 2), 0, 0);
                var grow = compositor.CreateVector3KeyFrameAnimation();
                grow.InsertKeyFrame(0, new Vector3(0.96f, 0.96f, 1));
                grow.InsertKeyFrame(1, Vector3.One);
                grow.Duration = duration;
                visual.StartAnimation("Scale", grow);
            }
        }
    }

    /// <summary>Rounds what an element holds: the room's drawing stops at the tile's corner.</summary>
    internal static void Round(FrameworkElement element, double corner)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var compositor = visual.Compositor;
        var geometry = compositor.CreateRoundedRectangleGeometry();
        geometry.Size = new Vector2((float)element.ActualWidth, (float)element.ActualHeight);
        geometry.CornerRadius = new Vector2((float)corner, (float)corner);
        visual.Clip = compositor.CreateGeometricClip(geometry);
    }
}

/// <summary>The Segoe Fluent Icons glyphs of the room's controls, as code points: no private-use character in the source.</summary>
internal static class RoomGlyphs
{
    public static string Close { get; } = char.ConvertFromUtf32(0xE711);
    public static string Ellipsis { get; } = char.ConvertFromUtf32(0xE712);
    public static string Stop { get; } = char.ConvertFromUtf32(0xE71A);
}

/// <summary>A count with its word, in a capsule washed with its colour, as Claudy's <c>RoomBadge</c>.</summary>
internal static class RoomBadge
{
    /// <param name="isDark">Lettered for a dark surface whatever the theme, as on the overview's tiles.</param>
    public static Border Make(string text, Ink ink, bool? isDark = null)
    {
        var dark = isDark ?? Ui.IsDark;
        var label = Ui.Text(text, 11, Ui.SemiBold, 1, Theme.Color(ink, dark));
        label.VerticalAlignment = VerticalAlignment.Center;
        return Ui.Capsule(new Border
        {
            Child = label,
            Padding = new Thickness(7, 0, 7, 0),
            MinHeight = 18,
            Background = Ui.Brush(Theme.Color(ink, isDark: true), dark ? 0.22 : 0.16),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }
}

/// <summary>
/// The projects with a session open, as the card's tabs, Claudy's <c>RoomSwitcher</c>: a capsule of
/// glass, the chosen one a brighter lens, a count beside each name only when something happens
/// there. Under the pointer a project shows a cross that puts its room away.
/// </summary>
internal sealed partial class RoomSwitcher : Grid
{
    private const double CrossSize = 14;
    private readonly RoomViewModel _room;
    private readonly StackPanel _segments = new() { Orientation = Orientation.Horizontal };
    private readonly Border _capsule;
    private string? _key;

    public RoomSwitcher(RoomViewModel room)
    {
        _room = room;
        MaxWidth = 620;
        VerticalAlignment = VerticalAlignment.Center;
        var height = Theme.Metric("tabHeight");
        _capsule = new Border
        {
            Child = _segments,
            Padding = new Thickness(3),
            CornerRadius = new CornerRadius((height + 6) / 2),
        };
        Children.Add(new ScrollViewer
        {
            Content = _capsule,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            HorizontalScrollMode = ScrollMode.Enabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Disabled,
        });
    }

    public void Update()
    {
        var projects = _room.Projects;
        var selected = _room.IsOverview ? null : _room.Selected?.Id;
        var key = string.Join('|', projects.Select(project => $"{project.Id}:{project.IsWaiting}:{project.Sessions.Count(session => session.IsWaiting)}:{project.RunningCount}"))
                  + $"|{selected}|{_room.IsOverview}|{Ui.IsDark}";
        if (key == _key)
        {
            return;
        }
        _key = key;
        Visibility = projects.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _capsule.Background = Ui.Primary(0.06);
        _segments.Children.Clear();
        if (projects.Count > 1)
        {
            var all = Segment(Ui.Text("All", 12, Ui.SemiBold), _room.IsOverview, () =>
            {
                _room.IsOverview = true;
                _room.IsSummaryOpen = false;
                _room.SelectedAgentId = null;
            }, "Every project at once");
            _segments.Children.Add(all);
        }
        foreach (var project in projects)
        {
            var isSelected = project.Id == selected;
            var name = Ui.Text(project.Id, 12, Ui.SemiBold);
            var row = Ui.Row(6, name);
            if (project.IsWaiting)
            {
                row.Children.Add(Ui.CountBadge(project.Sessions.Count(session => session.IsWaiting), Ink.Amber, showsHand: true));
            }
            else if (project.RunningCount > 0)
            {
                row.Children.Add(Ui.CountBadge(project.RunningCount, Ink.Coral));
            }
            var id = project.Id;
            // The cross makes its own room at the end of the name, so that nothing is covered.
            var cross = new Border
            {
                Child = Ui.Icon(RoomGlyphs.Close, 7, Ui.SecondaryOpacity),
                Width = CrossSize,
                Height = CrossSize,
                CornerRadius = new CornerRadius(CrossSize / 2),
                Background = Ui.Primary(0.12),
                Visibility = Visibility.Collapsed,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Ui.OnTap(cross, () => _room.Hide(id), $"Put away {id}");
            Ui.Tooltip(cross, "Put this room away");
            row.Children.Add(cross);
            var segment = Segment(row, isSelected, () => _room.Enter(id), null);
            Ui.Name(segment, isSelected ? $"{id}, selected" : id);
            segment.PointerEntered += (_, _) => cross.Visibility = Visibility.Visible;
            segment.PointerExited += (_, _) => cross.Visibility = Visibility.Collapsed;
            _segments.Children.Add(segment);
        }
    }

    private static Border Segment(FrameworkElement content, bool isSelected, Action action, string? help)
    {
        var height = Theme.Metric("tabHeight");
        content.VerticalAlignment = VerticalAlignment.Center;
        if (content is TextBlock label)
        {
            label.Foreground = isSelected ? Ui.Primary(1) : Ui.Secondary;
        }
        else if (content is Panel panel && panel.Children.Count > 0 && panel.Children[0] is TextBlock name)
        {
            name.Foreground = isSelected ? Ui.Primary(1) : Ui.Secondary;
        }
        var segment = new Border
        {
            Child = content,
            Height = height,
            Padding = new Thickness(14, 0, 14, 0),
            CornerRadius = new CornerRadius(height / 2),
            BorderThickness = new Thickness(0.5),
            Background = isSelected ? Ui.Brush(Ui.White, Ui.IsDark ? 0.16 : 0.85) : new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderBrush = isSelected ? Ui.Brush(Ui.White, Ui.IsDark ? 0.2 : 0.6) : null,
        };
        Ui.OnTap(segment, action, help);
        if (help is not null)
        {
            Ui.Tooltip(segment, help);
        }
        return segment;
    }
}
