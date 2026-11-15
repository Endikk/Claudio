using System.Globalization;
using System.Numerics;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.UI;
using Microsoft.Graphics.Canvas.UI.Xaml;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.UI.Text;

namespace Claudio.App;

/// <summary>
/// The scene the room shows at an instant. <paramref name="withHelp"/> asks for the words a pointer
/// is told (<see cref="RoomOccupant.Help"/> and the like): the drawing does without them.
/// </summary>
internal delegate RoomScene RoomSceneProvider(DateTimeOffset date, bool withHelp);

/// <summary>
/// The agent room, drawn: the open space of one project, redrawn thirty times a second while it
/// can be seen. Over the drawing lie the places a pointer can rest or click: each occupied desk
/// says who sits there, and Claudy's second screen opens the summary. Claudy's <c>RoomCanvas</c>;
/// what it draws comes from <see cref="Scene"/> and the properties beside it, which the room's view
/// model feeds.
/// </summary>
#pragma warning disable CA1001 // Its drawings belong to its device and are made again with it (CreateResources); a XAML element is never disposed.
internal sealed partial class RoomCanvas : Grid
#pragma warning restore CA1001
{
    /// <summary>Pixels per unit of the scene: a half unit is then one pixel at 100 %.</summary>
    public const double Unit = 2;

    public static Size Size { get; } = new(RoomGeometry.Size.Width * Unit, RoomGeometry.Size.Height * Unit);

    /// <summary>
    /// Frames a second, whatever the room shows: the vacuum glides, the rain falls, a Claudy walks
    /// without a jolt. The room stops drawing when it cannot be seen.
    /// </summary>
    private const double FrameRate = 30;

    private const double StepDuration = 0.18;
    private const double StrideDuration = 0.12;

    /// <summary>Seconds between two drawings of the sky and the city.</summary>
    private const double WindowPause = 0.2;

    /// <summary>
    /// The step every blink and every light is counted in, kept below a whole number of all their
    /// periods (2, 3 × 4, 5, 8, 9, 17, 23): counted from 2001 in steps of 0.18 s it would overflow.
    /// </summary>
    private const long StepCycle = 140_760;

    /// <summary>Claudy counts its time from 2001, as <c>timeIntervalSinceReferenceDate</c>: the vacuum is where Claudy's is.</summary>
    private static readonly DateTimeOffset ReferenceDate = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly CanvasControl _canvas;
    private readonly DispatcherQueueTimer _timer;
    private readonly Backdrop _backdrop = new();
    private CanvasRenderTarget? _window;
    private readonly NightLayers _night = new();
    private DateTimeOffset _windowDrawn;
    private bool _isRunning;
    private RoomScene? _hotScene;
    private DateTimeOffset _hotSceneAt;
    private string? _help;

    public RoomCanvas()
    {
        Width = Size.Width;
        Height = Size.Height;
        _canvas = new CanvasControl { Width = Size.Width, Height = Size.Height };
        _canvas.Draw += OnDraw;
        _canvas.CreateResources += (_, _) =>
        {
            // A new device, or a new scale: every kept drawing is made again.
            _backdrop.Reset();
            _window?.Dispose();
            _window = null;
            _night.Reset();
        };
        Children.Add(_canvas);

        _timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _timer.Tick += (_, _) => _canvas.Invalidate();

        PointerMoved += (_, args) => Hover(args.GetCurrentPoint(this).Position);
        PointerExited += (_, _) => ShowHelp(null, false);
        PointerPressed += OnPressed;
        Loaded += (_, _) =>
        {
            if (XamlRoot is { } root)
            {
                root.Changed += (_, _) => Resume();
            }
            Resume();
        };
        Unloaded += (_, _) => Resume();
        UpdateName();
    }

    // What the room draws, fed by its view model.

    /// <summary>The scene at an instant. Nothing is drawn without one.</summary>
    public RoomSceneProvider? Scene { get; set; }

    /// <summary>The project's name, on the board. Claudy's <c>room.selected?.id ?? "Claudy"</c>.</summary>
    public string Title
    {
        get;
        set
        {
            field = value;
            UpdateName();
        }
    } = RoomPainter.ChiefName;

    /// <summary>The tokens of the last seven days, each as a share of the busiest: the board's bars.</summary>
    public IReadOnlyList<double> Week { get; set; } = [];

    /// <summary>The session summary is open: Claudy's second screen stops calling for a click.</summary>
    public bool IsSummaryOpen { get; set; }

    /// <summary>The stored choice of weather: one of <see cref="RoomWeathers.Id"/>, or each day's own.</summary>
    public string WeatherChoice { get; set; } = RoomWeathers.AsItComes;

    /// <summary>The PC's clock, which lights the room and turns the wall clock's hands.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.Now;

    /// <summary>The PC's time zone unless set: the hour of the room is the hour on the wall.</summary>
    public TimeZoneInfo? TimeZone { get; set; }

    // What a click on the room says.

    /// <summary>An occupied desk was clicked: its index in <see cref="RoomScene.Desks"/>.</summary>
    public event Action<int>? DeskTapped;

    /// <summary>A Claudy on the sofa was clicked: its agent's id.</summary>
    public event Action<string>? LoungerTapped;

    /// <summary>Claudy's second screen was clicked: the summary opens or closes.</summary>
    public event Action? SummaryTapped;

    /// <summary>Draws a frame now, rather than at the next tick: the scene changed under a still room.</summary>
    public void Redraw() => _canvas.Invalidate();

    private void UpdateName() => AutomationProperties.SetName(this, $"Agent room of {Title}");

    /// <summary>The room is an ordinary window: hidden, minimised or closed, it stops drawing.</summary>
    private void Resume()
    {
        var isVisible = IsLoaded && XamlRoot is { IsHostVisible: true };
        if (isVisible == _isRunning)
        {
            return;
        }
        _isRunning = isVisible;
        if (isVisible)
        {
            // Under reduced motion the room still tells the time: one frame a second.
            _timer.Interval = TimeSpan.FromSeconds(Motion.ReducesMotion ? 1 : 1 / FrameRate);
            _timer.Start();
            _canvas.Invalidate();
        }
        else
        {
            _timer.Stop();
        }
    }

    private static double Seconds(DateTimeOffset date) => (date - ReferenceDate).TotalSeconds;

    private static int Step(double time) => (int)((long)Math.Floor(time / StepDuration) % StepCycle);

    private void OnDraw(CanvasControl sender, CanvasDrawEventArgs args) => Render(sender, args.DrawingSession);

    /// <summary>The frame on screen now, written to a PNG file: what a preview or a bug report shows.</summary>
    public async Task SaveFrameAsync(string path)
    {
        using var target = new CanvasRenderTarget(_canvas, (float)Size.Width, (float)Size.Height);
        using (var session = target.CreateDrawingSession())
        {
            session.Clear(Microsoft.UI.Colors.Transparent);
            Render(_canvas, session);
        }
        await target.SaveAsync(path, CanvasBitmapFileFormat.Png);
    }

    private void Render(ICanvasResourceCreatorWithDpi sender, CanvasDrawingSession context)
    {
        if (Scene is not { } provider)
        {
            return;
        }
        var date = Clock();
        var zone = TimeZone;
        var scene = provider(date, false);
        var sky = new RoomSky(RoomSky.HourAt(date, zone));
        var weather = RoomWeathers.Chosen(WeatherChoice, date, zone);
        var still = _backdrop.Image(sender, Title, Week, sky.HasLamps);
        var art = _backdrop.DeskArt(sender);
        var sign = RoomPainter.SignStyle;
        var plate = RoomPainter.NameplateStyle;
        var labels = scene.Zones.Select(zone => (zone.Title, sign.Size, sign.Weight, sign.Color))
                          .Append((RoomPainter.ChiefName, plate.Size, plate.Weight, plate.Color));
        var stamps = _backdrop.Stamps(sender, labels);
        Draw(context, scene, date, sky, weather, Week, still, art, stamps, Title, IsSummaryOpen, zone: zone, night: _night);
        DrawWindow(sender, context, date, sky, weather);
    }

    /// <summary>
    /// The window, over the room, in a drawing no larger than itself: its sky and its city a few
    /// times a second, which is all clouds and lights ask for, and what falls on a pass of its own
    /// at the room's rate.
    /// </summary>
    private void DrawWindow(ICanvasResourceCreatorWithDpi sender, CanvasDrawingSession context, DateTimeOffset date, RoomSky sky, RoomWeather weather)
    {
        var frame = RoomPainter.WindowFrame;
        var time = Seconds(date);
        var pause = Motion.ReducesMotion ? 1 : WindowPause;
        var since = (date - _windowDrawn).TotalSeconds;
        if (_window is null || since >= pause || since < 0)
        {
            _window ??= new CanvasRenderTarget(sender, (float)(frame.Width * Unit), (float)(frame.Height * Unit));
            using (var session = _window.CreateDrawingSession())
            {
                session.Clear(Microsoft.UI.Colors.Transparent);
                var painter = new RoomPainter(session, Unit, offset: new Vector2((float)(-frame.X * Unit), (float)(-frame.Y * Unit)));
                painter.Window(sky, weather, Step(time), time, RoomPainter.WindowPart.View);
            }
            _windowDrawn = date;
        }
        context.DrawImage(_window, new Vector2((float)(frame.X * Unit), (float)(frame.Y * Unit)));
        if (weather.IsFalling())
        {
            new RoomPainter(context, Unit).Window(sky, weather, Step(time), time, RoomPainter.WindowPart.Falling);
        }
    }

    // Pointer

    /// <summary>What lies under the pointer: what a click does, and what the pointer is told.</summary>
    private (Action? Act, string? Help) Spot(Point position, RoomScene scene)
    {
        double x = position.X / Unit, y = position.Y / Unit;
        var drop = RoomGeometry.CentreDrop;
        var summary = new Point(RoomPainter.QuotaCentre.X * Unit, (RoomPainter.QuotaCentre.Y + drop) * Unit);
        if (Math.Abs(position.X - summary.X) <= 22 && Math.Abs(position.Y - summary.Y) <= 22)
        {
            return (() => SummaryTapped?.Invoke(), IsSummaryOpen ? "Hide the session summary" : "Show the session summary");
        }
        if (scene.Chief.Help.Length > 0 && x >= 200 && x < 256 && y >= 100 + drop && y < 130 + drop)
        {
            return (null, scene.Chief.Help);
        }
        foreach (var lounger in scene.Loungers.Reverse())
        {
            var seat = RoomGeometry.Sofa(lounger.Slot);
            if (x >= seat.X - 8 && x < seat.X + 8 && y >= seat.Y - RoomSprite.Rows && y < seat.Y)
            {
                var id = lounger.Id;
                return (() => LoungerTapped?.Invoke(id), lounger.Help);
            }
        }
        for (var index = scene.Desks.Count - 1; index >= 0; index--)
        {
            if (scene.Desks[index] is not { } occupant)
            {
                continue;
            }
            var origin = RoomGeometry.Desk(Seat(index));
            var size = RoomGeometry.DeskSize;
            if (x >= origin.X && x < origin.X + size.Width && y >= origin.Y && y < origin.Y + size.Height)
            {
                var desk = index;
                return (() => DeskTapped?.Invoke(desk), occupant.Help);
            }
        }
        return (null, null);
    }

    /// <summary>The scene with its words, made again at most once a second, as Claudy's hotspots are.</summary>
    private RoomScene? HotScene()
    {
        if (Scene is not { } provider)
        {
            return null;
        }
        var now = Clock();
        if (_hotScene is null || Math.Abs((now - _hotSceneAt).TotalSeconds) >= 1)
        {
            _hotScene = provider(now, true);
            _hotSceneAt = now;
        }
        return _hotScene;
    }

    private void Hover(Point position)
    {
        if (HotScene() is not { } scene)
        {
            return;
        }
        var (act, help) = Spot(position, scene);
        ShowHelp(string.IsNullOrEmpty(help) ? null : help, act is not null);
    }

    private void ShowHelp(string? help, bool isClickable)
    {
        ProtectedCursor = isClickable ? InputSystemCursor.Create(InputSystemCursorShape.Hand) : null;
        if (help == _help)
        {
            return;
        }
        _help = help;
        ToolTipService.SetToolTip(_canvas, help);
    }

    private void OnPressed(object sender, PointerRoutedEventArgs args)
    {
        if (Scene is not { } provider)
        {
            return;
        }
        var (act, _) = Spot(args.GetCurrentPoint(this).Position, provider(Clock(), false));
        if (act is not null)
        {
            act();
            args.Handled = true;
            _canvas.Invalidate();
        }
    }

    // Drawing

    private static RoomSeat Seat(int index) => new(index / RoomPlan.SeatsPerZone, index % RoomPlan.SeatsPerZone);

    /// <summary>
    /// Draws a room. <paramref name="unit"/> is its scale and <paramref name="origin"/> its top left
    /// corner on the session; a small room, in the overview, has no image kept for it and draws its
    /// own window (<paramref name="drawsWindow"/>), where the full one leaves it to its layer.
    /// <paramref name="night"/> holds the drawing the night dims the room in, kept by the canvas;
    /// without it one is made for the frame.
    /// </summary>
    public static void Draw(CanvasDrawingSession context, RoomScene scene, DateTimeOffset date, RoomSky sky, RoomWeather weather,
                            IReadOnlyList<double> week, CanvasBitmap? still, RoomPainter.DeskArt? art,
                            IReadOnlyDictionary<string, RoomStamp> stamps, string title, bool isSummaryOpen, double unit = Unit,
                            bool drawsWindow = false, TimeZoneInfo? zone = null, Vector2 origin = default,
                            NightLayers? night = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scene);
        var painter = new RoomPainter(context, unit, stamps, origin);
        var time = Seconds(date);
        var step = Step(time);
        var quota = scene.Chief.Quota ?? 0;

        IReadOnlyList<RoomPoint> lit;
        if (sky.Dim is null)
        {
            lit = DrawRoom(painter, scene, date, sky, weather, week, still, art, title, isSummaryOpen, drawsWindow, zone, time, step, quota);
        }
        else
        {
            // The night multiplies the room by its colour and screens its lamps over it, which
            // Win2D does with effects: the room is drawn apart, in a bitmap the effects read.
            using var owned = night is null ? new NightLayers() : null;
            var room = (night ?? owned!).Ensure(context, new Vector2((float)(RoomGeometry.Size.Width * unit), (float)(RoomGeometry.Size.Height * unit)));
            using (var session = room.CreateDrawingSession())
            {
                session.Clear(Microsoft.UI.Colors.Transparent);
                lit = DrawRoom(new RoomPainter(session, unit, stamps), scene, date, sky, weather, week, still, art, title, isSummaryOpen, drawsWindow, zone, time, step, quota);
            }
            painter.Light(sky, lit, room);
        }
        if (drawsWindow && sky.Dim is not null)
        {
            painter.Window(sky, weather, step, time);
        }
        if (sky.Dim is null || !sky.HasLamps)
        {
            return;
        }
        // The night dims the screens with the rest: they are lit again on top of it.
        for (var index = 0; index < scene.Desks.Count; index++)
        {
            if (scene.Desks[index] is { } occupant)
            {
                painter.ScreenFace(RoomGeometry.Desk(Seat(index)), occupant, step);
            }
        }
        painter.Lowered(RoomGeometry.CentreDrop).ChiefScreens(scene.Chief, step, isSummaryOpen);
    }

    /// <summary>Everything under the light, back to front. Returns where the lit screens stand.</summary>
    private static List<RoomPoint> DrawRoom(RoomPainter painter, RoomScene scene, DateTimeOffset date, RoomSky sky, RoomWeather weather,
                                            IReadOnlyList<double> week, CanvasBitmap? still, RoomPainter.DeskArt? art, string title,
                                            bool isSummaryOpen, bool drawsWindow, TimeZoneInfo? zone, double time, int step, double quota)
    {
        var centre = painter.Lowered(RoomGeometry.CentreDrop);
        if (still is not null)
        {
            painter.Context.DrawImage(still, painter.Point(0, 0));
        }
        else
        {
            painter.StillBackdrop(title, week, sky.HasLamps);
        }
        painter.LiveBackdrop(sky, weather, step, time, date, quota, zone);
        if (drawsWindow)
        {
            painter.Window(sky, weather, step, time);
        }
        for (var index = 0; index < scene.Zones.Count && index < RoomGeometry.ZoneOrigins.Count; index++)
        {
            painter.Zone(scene.Zones[index], RoomGeometry.ZoneOrigins[index]);
        }

        // Back to front: whoever stands lower on the floor is drawn over what is behind.
        var desks = scene.Desks.Select((occupant, index) => (Origin: RoomGeometry.Desk(Seat(index)), Occupant: occupant)).ToList();
        RoomPoint? chosen = scene.SelectedDesk is { } selected && selected >= 0 && selected < desks.Count ? desks[selected].Origin : null;
        var layers = new List<(double Depth, Action<RoomPainter> Draw)>();
        foreach (var desk in desks)
        {
            layers.Add((desk.Origin.Y + RoomGeometry.DeskSize.Height, layer =>
            {
                layer.Desk(desk.Origin, desk.Occupant, step, art);
                if (desk.Origin == chosen)
                {
                    layer.Selection(desk.Origin);
                }
            }
            ));
        }
        foreach (var lounger in scene.Loungers)
        {
            layers.Add((RoomGeometry.Sofa(lounger.Slot).Y, layer => layer.Lounger(lounger, step)));
        }
        layers.Add((138 + RoomGeometry.CentreDrop, _ => centre.Chief(scene.Chief, step, sky.HasLamps, isSummaryOpen)));
        var vacuum = RoomVacuum.StateAt(time);
        painter.VacuumTrail(RoomVacuum.Trail(time));
        layers.Add((vacuum.Position.Y + 3, layer => layer.Vacuum(vacuum, time)));
        RoomSprite.Pose[] stride = [RoomSprite.Pose.Rest, RoomSprite.Pose.StepA, RoomSprite.Pose.Rest, RoomSprite.Pose.StepB];
        var pose = stride[(int)((long)Math.Floor(time / StrideDuration) % stride.Length)];
        foreach (var walker in scene.Walkers)
        {
            layers.Add((walker.Position.Y, layer =>
            {
                layer.Shadow(walker.Position.X - 6, walker.Position.Y - 1, 12, 2, opacity: 0.25);
                layer.Claudy(pose, walker.Position, walker.Tint);
                if (walker.Glyph is { } glyph)
                {
                    layer.BubbleOver(glyph, walker.Position.X - 5.5, walker.Position.Y - 28);
                }
            }
            ));
        }
        foreach (var layer in layers.OrderBy(layer => layer.Depth))
        {
            layer.Draw(painter);
        }

        painter.Vignette();
        painter.Strain(quota, time);
        return [.. desks.Where(desk => desk.Occupant is { State: not RoomOccupantState.Stalled })
                        .Select(desk => new RoomPoint(desk.Origin.X + 8.5, desk.Origin.Y + 8))];
    }

    /// <summary>The drawing the night dims and lights a room in, kept from one frame to the next.</summary>
    internal sealed class NightLayers : IDisposable
    {
        private CanvasRenderTarget? _room;

        public CanvasRenderTarget Ensure(ICanvasResourceCreatorWithDpi creator, Vector2 size)
        {
            if (_room is null || _room.Size.Width != size.X || _room.Size.Height != size.Y || _room.Dpi != creator.Dpi)
            {
                Reset();
                _room = new CanvasRenderTarget(creator, size.X, size.Y);
            }
            return _room;
        }

        public void Reset()
        {
            _room?.Dispose();
            _room = null;
        }

        public void Dispose() => Reset();

    }

    /// <summary>
    /// What never changes from one frame to the next, kept as drawings: the still part of the room,
    /// drawn again only when what it shows changes (the project's name, the week on the board, the
    /// lamps), a desk's furniture, drawn once, and the symbols and words stamped on every frame.
    /// All belong to one device and one scale, and go with them.
    /// </summary>
    private sealed class Backdrop
    {
        private string? _key;
        private CanvasRenderTarget? _still;
        private RoomPainter.DeskArt? _desk;
        private Dictionary<string, RoomStamp>? _stamps;

        public void Reset()
        {
            _still?.Dispose();
            _still = null;
            _key = null;
            _desk?.Chair.Dispose();
            _desk?.Desk.Dispose();
            _desk = null;
            foreach (var stamp in _stamps?.Values ?? Enumerable.Empty<RoomStamp>())
            {
                stamp.Image.Dispose();
            }
            _stamps = null;
        }

        public CanvasRenderTarget Image(ICanvasResourceCreatorWithDpi creator, string title, IReadOnlyList<double> week, bool isLit)
        {
            var wanted = string.Join('|', week.Select(share => share.ToString("R", CultureInfo.InvariantCulture))) + $"|{isLit}|{title}";
            if (wanted == _key && _still is not null)
            {
                return _still;
            }
            _key = wanted;
            _still?.Dispose();
            _still = Render(creator, RoomGeometry.Size, painter => painter.StillBackdrop(title, week, isLit));
            return _still;
        }

        public RoomPainter.DeskArt DeskArt(ICanvasResourceCreatorWithDpi creator)
        {
            if (_desk is not null)
            {
                return _desk;
            }
            var origin = new RoomPoint(RoomPainter.DeskArt.Margin, RoomPainter.DeskArt.Margin);
            _desk = new RoomPainter.DeskArt(Render(creator, RoomPainter.DeskArt.Size, painter => painter.DeskChair(origin)),
                                            Render(creator, RoomPainter.DeskArt.Size, painter => painter.DeskBody(origin)));
            return _desk;
        }

        /// <summary>
        /// Every symbol a screen can show, in each colour it is shown in, and the lines of text
        /// asked for, each made into a drawing the first time: laid out on every frame they cost
        /// far more than a drawing stamped.
        /// </summary>
        public Dictionary<string, RoomStamp> Stamps(ICanvasResourceCreator creator,
                                                             IEnumerable<(string Text, double Size, FontWeight Weight, uint Color)> labels)
        {
            if (_stamps is null)
            {
                _stamps = new Dictionary<string, RoomStamp>(StringComparer.Ordinal);
                foreach (var name in RoomGlyph.Names)
                {
                    foreach (var (color, points) in RoomGlyph.Inks)
                    {
                        _stamps[RoomGlyph.Tag(name, color, points)] = RoomSprites.Symbol(creator, name, points * Unit / 2, color);
                    }
                }
            }
            foreach (var (text, size, weight, color) in labels)
            {
                var tag = RoomPainter.LabelTag(text, size, weight, color);
                if (!_stamps.ContainsKey(tag))
                {
                    _stamps[tag] = RoomSprites.Label(creator, text, size * Unit / 2, weight, color);
                }
            }
            return _stamps;
        }

        /// <summary>A drawing of <paramref name="size"/> units, as an image at the canvas's scale.</summary>
        private static CanvasRenderTarget Render(ICanvasResourceCreatorWithDpi creator, RoomSize size, Action<RoomPainter> draw)
        {
            var target = new CanvasRenderTarget(creator, (float)(size.Width * Unit), (float)(size.Height * Unit));
            using var session = target.CreateDrawingSession();
            session.Clear(Microsoft.UI.Colors.Transparent);
            draw(new RoomPainter(session, Unit));
            return target;
        }
    }
}
