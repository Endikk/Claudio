using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.ViewModels;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App;

/// <summary>
/// Every project with a session open, each as a small room, side by side, as Claudy's
/// <c>RoomOverview</c>: who works where, at a glance. A click on a room enters it. The small rooms
/// are drawn once a second: nobody is seen walking here, only where everyone sits.
/// </summary>
internal sealed partial class RoomOverview : Grid
{
    private const int WideLimit = 4;
    private const double LabelHeight = 30;

    private readonly RoomViewModel _room;
    private readonly AgentsViewModel _agents;
    private readonly UsageViewModel _usage;
    private readonly Grid _grid = new();
    private readonly DispatcherQueueTimer _timer;
    private readonly List<(string Id, CanvasControl Canvas, ContentControl Label)> _tiles = [];
    private string? _layout;

    public RoomOverview(RoomViewModel room, AgentsViewModel agents, UsageViewModel usage)
    {
        _room = room;
        _agents = agents;
        _usage = usage;
        Width = RoomCanvas.Size.Width;
        Height = RoomCanvas.Size.Height;
        Background = Ui.Brush(Rgba.Parse("#1f1815"));
        var gap = Theme.Metric("gap");
        _grid.Padding = new Thickness(gap);
        _grid.ColumnSpacing = gap;
        _grid.RowSpacing = gap;
        Children.Add(new ScrollViewer
        {
            Content = _grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Interval = TimeSpan.FromSeconds(1);
        _timer.Tick += (_, _) =>
        {
            foreach (var tile in _tiles)
            {
                tile.Canvas.Invalidate();
            }
            UpdateLabels();
        };
        Loaded += (_, _) => _timer.Start();
        Unloaded += (_, _) => _timer.Stop();
    }

    private double? Quota => _usage.Snapshot.Session is { IsMeasured: true } session ? session.Percent : null;

    public void Update()
    {
        var projects = _room.Projects;
        var count = projects.Count <= WideLimit ? 2 : 3;
        var layout = $"{count}|{string.Join('|', projects.Select(project => project.Id))}";
        if (layout != _layout)
        {
            _layout = layout;
            Build(projects, count);
        }
        UpdateLabels();
    }

    private void Build(ValueList<RoomViewModel.Project> projects, int count)
    {
        foreach (var tile in _tiles)
        {
            tile.Canvas.RemoveFromVisualTree();
        }
        _tiles.Clear();
        _grid.Children.Clear();
        _grid.ColumnDefinitions.Clear();
        _grid.RowDefinitions.Clear();
        var gap = Theme.Metric("gap");
        var width = Math.Floor((RoomCanvas.Size.Width - (gap * (count + 1))) / count);
        var unit = width / RoomGeometry.Size.Width;
        for (var column = 0; column < count; column++)
        {
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(width) });
        }
        for (var index = 0; index < projects.Count; index++)
        {
            if (index % count == 0)
            {
                _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }
            var id = projects[index].Id;
            var canvas = new CanvasControl { Width = width, Height = RoomGeometry.Size.Height * unit };
            canvas.Draw += (sender, args) => Draw(id, unit, args);
            var label = new ContentControl
            {
                IsTabStop = false,
                Height = LabelHeight,
                Padding = new Thickness(10, 0, 10, 0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            var tile = new Border
            {
                Child = Ui.Column(0, canvas, label),
                Background = Ui.Brush(Rgba.Parse("#2a211d")),
                CornerRadius = new CornerRadius(12),
                BorderBrush = Ui.Brush(Ui.White, 0.1),
                BorderThickness = new Thickness(1),
            };
            tile.Loaded += (_, _) => RoomView.Round(tile, 12);
            Ui.OnTap(tile, () => _room.Enter(id));
            Ui.Tooltip(tile, $"Enter the room of {id}");
            Grid.SetColumn(tile, index % count);
            Grid.SetRow(tile, index / count);
            _grid.Children.Add(tile);
            _tiles.Add((id, canvas, label));
        }
    }

    private void Draw(string id, double unit, CanvasDrawEventArgs args)
    {
        if (_room.Projects.FirstOrDefault(project => project.Id == id) is not { } project)
        {
            return;
        }
        var date = DateTimeOffset.Now;
        var scene = _room.Scene(project, date, Quota, withHelp: false);
        var sky = new RoomSky(RoomSky.HourAt(date));
        var weather = RoomWeathers.Chosen(_room.WeatherChoice, date);
        RoomCanvas.Draw(args.DrawingSession, scene, date, sky, weather, [], null, null, new Dictionary<string, RoomStamp>(), project.Id,
                        isSummaryOpen: true, unit: unit, drawsWindow: true);
    }

    private void UpdateLabels()
    {
        foreach (var (id, _, label) in _tiles)
        {
            if (_room.Projects.FirstOrDefault(project => project.Id == id) is not { } project)
            {
                continue;
            }
            var tally = new RoomTally(project, _agents.Now);
            var key = $"{tally}";
            if (label.Tag as string == key)
            {
                continue;
            }
            label.Tag = key;
            var name = Ui.Text(project.Id, 12, Ui.SemiBold, 1, RoomSummary.Ink);
            var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
            if (tally.Waiting > 0)
            {
                badges.Children.Add(RoomBadge.Make($"{tally.Waiting} waiting", Ink.Amber, isDark: true));
            }
            if (tally.Working > 0)
            {
                badges.Children.Add(RoomBadge.Make($"{tally.Working} working", Ink.Coral, isDark: true));
            }
            if (tally.Stuck > 0)
            {
                badges.Children.Add(RoomBadge.Make($"{tally.Stuck} stuck", Ink.Danger, isDark: true));
            }
            label.Content = Ui.Spread(name, badges, minimumGap: 4);
            Ui.Name(label, $"{project.Id}, {project.RunningCount} agents");
        }
    }
}
