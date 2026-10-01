using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Claudio.App.Views;

/// <summary>
/// One quota as a ring: the arc fills to the percentage, the dot on the track marks how much of the
/// window has elapsed. Arc past the dot means ahead of pace. The arc grows from zero each time the
/// flyout opens.
/// </summary>
internal sealed partial class QuotaRing : StackPanel
{
    private const double Diameter = 60;
    private const double LineWidth = 6;

    private readonly Canvas _canvas = new() { Width = Diameter, Height = Diameter };
    private readonly TextBlock _value = Ui.Text(string.Empty, 16, Ui.SemiBold);
    private readonly TextBlock _title = Ui.Micro(string.Empty, 0.55);
    private readonly TextBlock _when = Ui.Text(string.Empty, 9.5, Ui.Medium, 0.4);
    private readonly SpringValue _shown;
    private readonly double _delay;
    private UsageWindow? _window;
    private Rgba _tint;
    private double _target;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _stagger;

    public QuotaRing(double delay = 0)
    {
        _delay = delay;
        Spacing = 6;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        var figure = _value;
        figure.HorizontalAlignment = HorizontalAlignment.Center;
        figure.VerticalAlignment = VerticalAlignment.Center;
        var face = new Grid { Width = Diameter, Height = Diameter, HorizontalAlignment = HorizontalAlignment.Center, Children = { _canvas, figure } };
        _title.HorizontalAlignment = HorizontalAlignment.Center;
        _when.HorizontalAlignment = HorizontalAlignment.Center;
        Children.Add(face);
        Children.Add(Ui.Column(1, _title, _when));
        _shown = new SpringValue(SpringValue.Gauge, _ => Draw());
    }

    public void Update(UsageWindow window, DateTimeOffset now)
    {
        _window = window;
        _tint = Theme.Tint(window.Accent, window.Percent);
        _target = window.IsMeasured ? Math.Clamp(window.Percent, 0, 1) : 0;
        Ui.Figure(_value, UsageFormat.Percent(window), Ui.Primary(window.IsMeasured ? 0.92 : 0.4), window.IsMeasured,
                  9, Ui.Primary(0.4), Ui.Medium);
        Ui.Restyle(_title, $"{window.Title} · {window.Window}".ToUpperInvariant(), 0.55);
        Ui.Restyle(_when, window.IsActive(now) ? $"in {UsageFormat.Countdown(window.ResetDate, now)}" : (window.IsMeasured ? "inactive" : "unavailable"), 0.4);
        Ui.Tooltip(this, window.IsActive(now)
            ? $"{window.Title} · {window.Window}, resets {UsageFormat.ResetTime(window.ResetDate, now)}. The dot marks the time elapsed in the window."
            : $"{window.Title} · {window.Window}");
        _shown.Set(_target);
        Draw();
    }

    /// <summary>The arc grows again from zero, staggered ring by ring.</summary>
    public void Replay()
    {
        _shown.Set(0, animated: false);
        // Kept in a field: a timer nothing holds can be collected before it fires.
        _stagger ??= DispatcherQueue.CreateTimer();
        _stagger.Stop();
        _stagger.Interval = TimeSpan.FromSeconds(Math.Max(_delay, 0.001));
        _stagger.IsRepeating = false;
        _stagger.Tick -= Grow;
        _stagger.Tick += Grow;
        _stagger.Start();
    }

    private void Grow(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args) => _shown.Set(_target);

    private void Draw()
    {
        _canvas.Children.Clear();
        // SwiftUI strokes the circle on its path, half inside the frame and half outside.
        const double Radius = Diameter / 2;
        const double Centre = Diameter / 2;
        var track = new Ellipse
        {
            Width = Diameter + LineWidth,
            Height = Diameter + LineWidth,
            Stroke = Ui.Primary(0.08),
            StrokeThickness = LineWidth,
        };
        Canvas.SetLeft(track, -LineWidth / 2);
        Canvas.SetTop(track, -LineWidth / 2);
        _canvas.Children.Add(track);

        var fraction = Math.Clamp(_shown.Value, 0, 0.9999);
        if (fraction > 0.001)
        {
            var end = Angle(fraction);
            var arc = new PathFigure { StartPoint = Point(Centre, Radius, 0) };
            arc.Segments.Add(new ArcSegment
            {
                Point = end,
                Size = new Size(Radius, Radius),
                SweepDirection = SweepDirection.Clockwise,
                IsLargeArc = fraction > 0.5,
            });
            var geometry = new PathGeometry();
            geometry.Figures.Add(arc);
            _canvas.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = geometry,
                Stroke = Ui.Brush(_tint),
                StrokeThickness = LineWidth,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
            });
        }

        if (_window is { } window && window.IsActive(DateTimeOffset.UtcNow))
        {
            var pace = Point(Centre, Radius, window.Elapsed(DateTimeOffset.UtcNow));
            var dot = new Ellipse { Width = 4, Height = 4, Fill = Ui.Primary(0.75) };
            Canvas.SetLeft(dot, pace.X - 2);
            Canvas.SetTop(dot, pace.Y - 2);
            _canvas.Children.Add(dot);
        }

        Point Angle(double share) => Point(Centre, Radius, share);
    }

    /// <summary>The point at <paramref name="share"/> of a turn, clockwise from the top.</summary>
    private static Point Point(double centre, double radius, double share)
    {
        var angle = (share * 2 * Math.PI) - (Math.PI / 2);
        return new Point(centre + (radius * Math.Cos(angle)), centre + (radius * Math.Sin(angle)));
    }
}
