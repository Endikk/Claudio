using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using Claudio.Core.Design;
using Claudio.Core.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Shape = Microsoft.UI.Xaml.Shapes.Path;

namespace Claudio.App.Views;

/// <summary>
/// Claudio at the top of the screen, as Claudy's <c>NotchView</c> round the notch. At rest a band the
/// notch's height extends it on both sides, in the card's glass: the mascot in the left ear, the lead
/// percentage in the right one. Open, the island's own view (<see cref="NotchActivityView"/>) hangs
/// underneath on the same glass, the mascot and the figure flying into it from their ears. The island
/// is dark whatever the system's theme, as Claudy's is, grown out of the black notch.
/// </summary>
internal sealed partial class NotchView : Grid
{
    private const double RestingRadius = 9;
    private const double OpenRadius = 20;

    /// <summary>The distance over which the screen's top edge darkens into the glass.</summary>
    private const double BlackFade = 12;

    /// <summary>Opening drops from the top with a slight bounce, as the Dynamic Island does; closing goes back quicker, without one.</summary>
    private static readonly Spring Opening = new(0.42, 0.74);
    private static readonly Spring Closing = new(0.3, 0.92);

    private readonly UsageViewModel _model;
    private readonly UpdateChecker _updates;
    private readonly Canvas _canvas = new();
    private readonly Image _shadow = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Shape _glass = new();
    private readonly Shape _sheen = new() { IsHitTestVisible = false };
    private readonly Shape _glow = new() { IsHitTestVisible = false };
    private readonly Shape _shade = new() { IsHitTestVisible = false };
    private readonly Shape _rim = new() { StrokeThickness = 1, IsHitTestVisible = false };
    private readonly RadialGradientBrush _glowInk;
    private readonly Canvas _clipped = new();
    private readonly CompositionRoundedRectangleGeometry _clip;
    private readonly NotchActivityView _content;
    private readonly ScaleTransform _contentScale = new();
    private readonly MascotView _restMascot = new();
    private readonly MascotView _flyMascot = new() { Width = NotchFlight.MascotWidth, Height = NotchFlight.MascotSize };
    private readonly CompositeTransform _mascotFlight = new();
    private readonly IslandPercent _restFigure = new(IslandPercent.RestingSize) { IsHitTestVisible = false };
    private readonly IslandPercent _flyFigure = new() { IsHitTestVisible = false };
    private readonly CompositeTransform _figureFlight = new();
    private readonly StackPanel _dots = new() { Spacing = 3, IsHitTestVisible = false };
    private readonly Stopwatch _fadeClock = new();
    private readonly Stopwatch _frameClock = new();

    private Size _notch = new(24, 32);
    private Size _openContent;
    private Point _mascotLanding;
    private Point _figureLanding;
    private bool _isOpen;
    private double _progress;
    private double _velocity;
    private Spring _spring = Opening;
    private bool _isAnimating;
    private double _fade;
    private (Size Body, double Scale) _shadowKey;
    private ScreenSize _reported;

    public NotchView(UsageViewModel model, UpdateChecker updates)
    {
        _model = model;
        _updates = updates;
        _content = new NotchActivityView(model, updates) { Opacity = 0, Visibility = Visibility.Collapsed, RenderTransform = _contentScale };
        RequestedTheme = ElementTheme.Dark;
        Background = new SolidColorBrush(Colors.Transparent);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, "Claudio");

        // Claudy's glass: a dark body, a light sheen from the top-left corner and a coral halo there.
        _glass.Fill = Ui.Brush(new Rgba(0x1E, 0x1D, 0x1C));
        var white = new Rgba(255, 255, 255);
        var sheen = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        sheen.GradientStops.Add(new GradientStop { Color = Ui.Color(white, 0.10), Offset = 0 });
        sheen.GradientStops.Add(new GradientStop { Color = Ui.Color(white, 0.015), Offset = 1 });
        _sheen.Fill = sheen;
        var coral = Theme.Color(Accent.Coral);
        _glowInk = new RadialGradientBrush { MappingMode = BrushMappingMode.Absolute, RadiusX = 240, RadiusY = 240 };
        _glowInk.GradientStops.Add(new GradientStop { Color = Ui.Color(coral, 0.16), Offset = 0 });
        _glowInk.GradientStops.Add(new GradientStop { Color = Ui.Color(coral, 0), Offset = 1 });
        _glow.Fill = _glowInk;
        // The screen's top edge darkens into the glass, so the island melts into the edge it hangs
        // from instead of meeting it along a line.
        var black = new Rgba(0, 0, 0);
        var shade = new LinearGradientBrush { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, 0), EndPoint = new Point(0, BlackFade) };
        shade.GradientStops.Add(new GradientStop { Color = Ui.Color(black, 0.55), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = Ui.Color(black, 0), Offset = 1 });
        _shade.Fill = shade;
        // The card's hairline, faded out towards the top: the island's top edge is the screen's.
        var rim = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        rim.GradientStops.Add(new GradientStop { Color = Ui.Color(white, 0), Offset = 0 });
        rim.GradientStops.Add(new GradientStop { Color = Ui.Color(white, 0.16), Offset = 1 });
        _rim.Stroke = rim;

        _flyMascot.RenderTransform = _mascotFlight;
        _flyFigure.RenderTransform = _figureFlight;
        _restMascot.IsHitTestVisible = false;
        _flyMascot.IsHitTestVisible = false;
        _clipped.Children.Add(_content);
        _clipped.Children.Add(_restMascot);
        _clipped.Children.Add(_restFigure);
        _clipped.Children.Add(_dots);
        _clipped.Children.Add(_flyMascot);
        _clipped.Children.Add(_flyFigure);
        foreach (var layer in new UIElement[] { _shadow, _glass, _sheen, _glow, _shade, _clipped, _rim })
        {
            _canvas.Children.Add(layer);
        }
        Children.Add(_canvas);

        var visual = ElementCompositionPreview.GetElementVisual(_clipped);
        _clip = visual.Compositor.CreateRoundedRectangleGeometry();
        visual.Clip = visual.Compositor.CreateGeometricClip(_clip);

        SizeChanged += (_, _) => Apply();
        Unloaded += (_, _) => StopAnimating();
    }

    /// <summary>
    /// The island's size, the shadow's margin included once open: the window follows it. The
    /// shape's target, not each frame of the spring: the window grows at once and shrinks late.
    /// </summary>
    public event Action<ScreenSize>? ShapeChanged;

    public bool IsOpen => _isOpen;

    /// <summary>The notch the ears hug, in units.</summary>
    public Size Notch
    {
        get => _notch;
        set
        {
            if (_notch != value)
            {
                _notch = value;
                Update();
            }
        }
    }

    /// <summary>The island's width once open: known before it opens, so the window can widen first.</summary>
    public double OpenWidth => Math.Max(_content.Width, EarsWidth) + (2 * NotchLayout.Shoulder) + (2 * CardShadow.Inset);

    private double EarsWidth => _notch.Width + (2 * NotchLayout.EarWidth);

    public void Open(bool animated) => Turn(open: true, animated);

    public void Close(bool animated) => Turn(open: false, animated);

    /// <summary>Redraws from the model, dark whatever the system's theme.</summary>
    public void Update()
    {
        var was = Ui.IsDark;
        Ui.IsDark = true;
        try
        {
            Redraw();
        }
        finally
        {
            Ui.IsDark = was;
        }
    }

    private void Redraw()
    {
        var snapshot = _model.Snapshot;
        var lead = snapshot.Primary;
        var now = DateTimeOffset.UtcNow;
        var tint = Theme.Tint(lead.Accent, lead.Percent);
        foreach (var mascot in new[] { _restMascot, _flyMascot })
        {
            mascot.Tint = tint;
            mascot.IsOverloaded = snapshot.IsOverloaded;
            mascot.IsWaving = _updates.IsGreeting && !snapshot.IsOverloaded;
            mascot.IsTyping = snapshot.Session.IsRunning(now);
        }
        _restFigure.Update(lead);
        _flyFigure.Update(lead);
        var percent = lead.IsMeasured ? $"{Claudio.Core.Presentation.UsageFormat.Percent(lead)}%" : Claudio.Core.Presentation.UsageFormat.NoFigure;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(this, $"Claudio, {lead.Title} {percent}");

        _dots.Children.Clear();
        if (_updates.Available is not null)
        {
            _dots.Children.Add(Ui.Dot(5, Theme.Color(Accent.Coral)));
        }
        if (_model.ErrorMessage is { } message)
        {
            var dot = Ui.Dot(5, Theme.Danger);
            Ui.Tooltip(dot, message);
            _dots.Children.Add(dot);
        }

        _content.Notch = _notch;
        _content.Update();
        MeasureOpen();
        Report();
        Apply();
    }

    /// <summary>The open view's size, and where the mascot and the figure land in it.</summary>
    private void MeasureOpen()
    {
        var shown = _content.Visibility;
        _content.Visibility = Visibility.Visible;
        _content.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _openContent = _content.DesiredSize;
        _content.UpdateLayout();
        _mascotLanding = _content.MascotLanding;
        _figureLanding = _content.FigureLanding;
        _content.Visibility = shown;

        // The crisp mascot of the ears: one device pixel per sprite pixel, the sprite halved below
        // 160 %, where a third of the landed mascot would fall between pixels.
        var scale = XamlRoot?.RasterizationScale ?? 1;
        var halved = scale < 1.6;
        var sprite = Mascot.Shared.Poses["resting"];
        var frame = halved ? TrayIconImage.Halve(sprite) : sprite;
        _restMascot.IsHalved = halved;
        _restMascot.Width = frame.Columns / scale;
        _restMascot.Height = frame.Rows.Count / scale;
        _flyFigure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        _restFigure.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
    }

    /// <summary>The size the window must hold for the shape's target.</summary>
    private void Report()
    {
        var size = _isOpen
            ? new ScreenSize(OpenWidth, Math.Max(_openContent.Height, _notch.Height) + CardShadow.Inset)
            : new ScreenSize(EarsWidth + (2 * NotchLayout.Shoulder), _notch.Height);
        if (size != _reported)
        {
            _reported = size;
            ShapeChanged?.Invoke(size);
        }
    }

    private void Turn(bool open, bool animated)
    {
        if (open == _isOpen)
        {
            return;
        }
        _isOpen = open;
        _spring = open ? Opening : Closing;
        if (open)
        {
            _content.Visibility = Visibility.Visible;
            Update();
        }
        Report();
        if (!animated)
        {
            _progress = open ? 1 : 0;
            _velocity = 0;
            _fade = open ? 1 : 0;
            StopAnimating();
            Finish();
            return;
        }
        _fadeClock.Restart();
        if (!_isAnimating)
        {
            _isAnimating = true;
            CompositionTarget.Rendering += Step;
        }
        Apply();
    }

    private void Step(object? sender, object e)
    {
        var dt = Math.Min(_frameClock.IsRunning ? _frameClock.Elapsed.TotalSeconds : 1.0 / 60, 1.0 / 30);
        _frameClock.Restart();
        var omega = 2 * Math.PI / _spring.ResponseSeconds;
        var stiffness = omega * omega;
        var damping = 2 * _spring.DampingFraction * omega;
        var target = _isOpen ? 1.0 : 0.0;
        // Semi-implicit Euler, split into small steps so a stiff spring stays stable.
        const int Substeps = 4;
        for (var step = 0; step < Substeps; step++)
        {
            var h = dt / Substeps;
            _velocity += ((-stiffness * (_progress - target)) - (damping * _velocity)) * h;
            _progress += _velocity * h;
        }

        // The content settles in just after the shape starts to drop, and leaves first on the way
        // back so the shape closes on nothing.
        var faded = _fadeClock.Elapsed.TotalSeconds;
        double fadeEnd;
        if (_isOpen)
        {
            var t = Math.Clamp((faded - 0.05) / 0.28, 0, 1);
            _fade = 1 - ((1 - t) * (1 - t));
            fadeEnd = 0.33;
        }
        else
        {
            var t = Math.Clamp(faded / 0.12, 0, 1);
            _fade = Math.Min(_fade, 1 - (t * t));
            fadeEnd = 0.12;
        }

        var settled = Math.Abs(_progress - target) < 1e-3 && Math.Abs(_velocity) < 1e-2 && faded >= fadeEnd;
        if (settled)
        {
            _progress = target;
            _velocity = 0;
            StopAnimating();
            Finish();
            return;
        }
        Apply();
    }

    private void StopAnimating()
    {
        if (_isAnimating)
        {
            CompositionTarget.Rendering -= Step;
            _isAnimating = false;
        }
        _frameClock.Reset();
    }

    private void Finish()
    {
        _fade = _isOpen ? 1 : 0;
        _content.Visibility = _isOpen ? Visibility.Visible : Visibility.Collapsed;
        Apply();
    }

    /// <summary>Lays the island out for the current step of the spring, centred in the window.</summary>
    private void Apply()
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }
        var p = _progress;
        var q = Math.Clamp(p, 0, 1);
        static double Lerp(double from, double to, double t) => from + ((to - from) * t);

        var shoulder = NotchLayout.Shoulder;
        var restWidth = EarsWidth + (2 * shoulder);
        var openInner = Math.Max(_content.Width, EarsWidth);
        var islandWidth = Math.Max(Lerp(restWidth, openInner + (2 * shoulder), p), 2 * shoulder);
        var islandHeight = Math.Max(Lerp(_notch.Height, Math.Max(_openContent.Height, _notch.Height), p), 1);
        var radius = Math.Max(Lerp(RestingRadius, OpenRadius, p), 0);
        var left = (width - islandWidth) / 2;
        var rect = new ScreenRect(left, 0, islandWidth, islandHeight);

        foreach (var path in new[] { _glass, _sheen, _glow, _shade, _rim })
        {
            path.Data = Geometry(rect, radius);
        }
        _glowInk.Center = new Point(left, 0);
        _glowInk.GradientOrigin = new Point(left, 0);

        // The card's shadow, open only: at rest the ears meet the top edge, and a margin round them
        // would take clicks from what lies beside them.
        var body = new Size(Math.Max(_content.Width, EarsWidth), Math.Max(_openContent.Height, _notch.Height));
        DrawShadow(body);
        var inset = CardShadow.Inset;
        _shadow.Opacity = q;
        _shadow.Visibility = q > 0.001 ? Visibility.Visible : Visibility.Collapsed;
        _shadow.Width = islandWidth - (2 * shoulder) + (2 * inset);
        _shadow.Height = islandHeight + inset;
        Canvas.SetLeft(_shadow, left + shoulder - inset);

        _clip.Offset = new System.Numerics.Vector2((float)(left + shoulder), (float)-radius);
        _clip.Size = new System.Numerics.Vector2((float)Math.Max(islandWidth - (2 * shoulder), 0), (float)(islandHeight + radius));
        _clip.CornerRadius = new System.Numerics.Vector2((float)radius, (float)radius);

        var contentLeft = (width - _content.Width) / 2;
        Canvas.SetLeft(_content, contentLeft);
        _content.Opacity = _fade;
        _contentScale.CenterX = _content.Width / 2;
        _contentScale.ScaleX = _contentScale.ScaleY = 1 - (0.06 * (1 - _fade));

        // The ears: the mascot in the left one, the figure in the right one, both centred on the
        // notch's height. Open, both fly to where the open view marks them.
        var earsLeft = (width - EarsWidth) / 2;
        var mascotRest = new Point(earsLeft + (NotchLayout.EarWidth / 2), _notch.Height / 2);
        var figureRest = new Point(earsLeft + EarsWidth - (NotchLayout.EarWidth / 2), _notch.Height / 2);
        var mascotLanded = new Point(contentLeft + _mascotLanding.X, _mascotLanding.Y);
        var figureLanded = new Point(contentLeft + _figureLanding.X, _figureLanding.Y);

        var resting = !_isOpen && !_isAnimating;
        _restMascot.Visibility = _restFigure.Visibility = resting ? Visibility.Visible : Visibility.Collapsed;
        _flyMascot.Visibility = _flyFigure.Visibility = resting ? Visibility.Collapsed : Visibility.Visible;
        Place(_restMascot, mascotRest, _restMascot.Width, _restMascot.Height);
        Place(_restFigure, figureRest, _restFigure.DesiredSize.Width, _restFigure.DesiredSize.Height);
        Fly(_flyMascot, _mascotFlight, Lerp(mascotRest.X, mascotLanded.X, p), Lerp(mascotRest.Y, mascotLanded.Y, p),
            Lerp(1.0 / 3, 1, p), _flyMascot.Width, _flyMascot.Height);
        Fly(_flyFigure, _figureFlight, Lerp(figureRest.X, figureLanded.X, p), Lerp(figureRest.Y, figureLanded.Y, p),
            Lerp(IslandPercent.RestingSize / IslandPercent.LandedSize, 1, p), _flyFigure.DesiredSize.Width, _flyFigure.DesiredSize.Height);

        // The open view carries its own update row and error line.
        _dots.Visibility = _isOpen || _dots.Children.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _dots.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(_dots, earsLeft + EarsWidth - 2 - _dots.DesiredSize.Width);
        Canvas.SetTop(_dots, (_notch.Height - _dots.DesiredSize.Height) / 2);
    }

    private static void Place(FrameworkElement element, Point centre, double width, double height)
    {
        var scale = element.XamlRoot?.RasterizationScale ?? 1;
        // Whole device pixels, or the pixel art and the figure blur.
        Canvas.SetLeft(element, Math.Round((centre.X - (width / 2)) * scale) / scale);
        Canvas.SetTop(element, Math.Round((centre.Y - (height / 2)) * scale) / scale);
    }

    private static void Fly(FrameworkElement element, CompositeTransform transform, double x, double y, double scale, double width, double height)
    {
        transform.CenterX = width / 2;
        transform.CenterY = height / 2;
        transform.ScaleX = transform.ScaleY = scale;
        transform.TranslateX = x - (width / 2);
        transform.TranslateY = y - (height / 2);
    }

    private static PathGeometry Geometry(ScreenRect rect, double radius)
    {
        var (startX, startY, segments) = NotchShape.Path(rect, radius, NotchLayout.Shoulder);
        var figure = new PathFigure { StartPoint = new Point(startX, startY), IsClosed = true, IsFilled = true };
        foreach (var segment in segments)
        {
            figure.Segments.Add(segment switch
            {
                OutlineCurve curve => new QuadraticBezierSegment { Point1 = new Point(curve.ControlX, curve.ControlY), Point2 = new Point(curve.X, curve.Y) },
                OutlineArc arc => new Microsoft.UI.Xaml.Media.ArcSegment
                {
                    Point = new Point(arc.X, arc.Y),
                    Size = new Size(arc.Radius, arc.Radius),
                    SweepDirection = SweepDirection.Clockwise,
                },
                _ => new Microsoft.UI.Xaml.Media.LineSegment { Point = new Point(segment.X, segment.Y) },
            });
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }

    /// <summary>The open island's shadow, drawn once per size and scale, then stretched with the spring.</summary>
    private void DrawShadow(Size body)
    {
        var scale = XamlRoot?.RasterizationScale ?? 1;
        if (_shadowKey == (body, scale) || body.Width <= 0 || body.Height <= 0)
        {
            return;
        }
        _shadowKey = (body, scale);
        var (pixels, width, height) = CardShadow.Island(body.Width, body.Height, OpenRadius, scale);
        var bitmap = new WriteableBitmap(width, height);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(pixels);
        }
        bitmap.Invalidate();
        _shadow.Source = bitmap;
        // Corners and edges keep their blur; only the middle stretches while the island moves.
        var edge = Math.Round((CardShadow.Inset + OpenRadius) * scale);
        _shadow.NineGrid = new Thickness(edge, 0, edge, edge);
    }
}
