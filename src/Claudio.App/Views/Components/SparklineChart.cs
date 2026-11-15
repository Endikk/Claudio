using System.Globalization;
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
/// Seven days of tokens as a sparkline, as Claudy's <c>SparklineChart</c>. No axes and no grid: the
/// shape and the last point are enough. The header carries today's and the week's totals; hovering
/// a day puts its own figures there.
/// </summary>
internal sealed partial class SparklineChart : StackPanel
{
    private const double PlotHeight = 64;

    private readonly TextBlock _title;
    private readonly TextBlock _day = Ui.Meta(string.Empty);
    private readonly TextBlock _tokens = Ui.Text(string.Empty, 13, Ui.SemiBold);
    private readonly TextBlock _share = Ui.Meta(string.Empty);
    private readonly TextBlock _todayLabel = Ui.Meta("Today");
    private readonly TextBlock _todayValue = Ui.Text(string.Empty, 13, Ui.SemiBold);
    private readonly TextBlock _weekLabel = Ui.Meta("7 days");
    private readonly TextBlock _weekValue = Ui.Text(string.Empty, 13, Ui.SemiBold);
    private readonly Grid _hoverHeader;
    private readonly Grid _totalsHeader;
    private long? _today;
    private long? _week;
    private readonly Canvas _plot = new() { Height = PlotHeight, Background = Ui.Primary(0) };
    private readonly Grid _axis = new();
    private readonly SpringValue _growth;
    private IReadOnlyList<TokenSample> _samples = [];
    private Rgba _tint;
    private int? _hovered;

    public SparklineChart(string title)
    {
        Spacing = 12;
        _title = Ui.SectionLabel(title);
        _hoverHeader = Ui.Spread(Ui.Row(6, _day, _tokens), _share, VerticalAlignment.Bottom, minimumGap: 4);
        _totalsHeader = Ui.Spread(Ui.Row(4, _todayLabel, _todayValue), Ui.Row(4, _weekLabel, _weekValue), VerticalAlignment.Bottom, minimumGap: 8);
        var header = new Grid { Height = 16, Children = { _title, _hoverHeader, _totalsHeader } };
        _title.VerticalAlignment = VerticalAlignment.Bottom;
        Children.Add(header);
        Children.Add(Ui.Column(6, _plot, _axis));
        _growth = new SpringValue(SpringValue.Gauge, _ => Draw(), initial: 0);
        _plot.SizeChanged += (_, _) => Draw();
        _plot.PointerMoved += (_, args) => Hover(args.GetCurrentPoint(_plot).Position.X);
        _plot.PointerExited += (_, _) => Hover(null);
    }

    public void Update(IReadOnlyList<TokenSample> samples, Rgba tint, long? today = null, long? week = null)
    {
        _today = today;
        _week = week;
        var changed = !samples.Select(sample => sample.Tokens).SequenceEqual(_samples.Select(sample => sample.Tokens));
        _samples = samples;
        _tint = tint;
        _title.Foreground = Ui.Secondary;
        _axis.ColumnDefinitions.Clear();
        _axis.Children.Clear();
        for (var index = 0; index < samples.Count; index++)
        {
            _axis.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        if (changed)
        {
            // The curve rises from the floor whenever its data moves, as SwiftUI animates it.
            _growth.Set(0, animated: false);
            _growth.Set(1);
        }
        Draw();
    }

    private void Hover(double? x)
    {
        int? index = null;
        if (x is { } position && _samples.Count > 0 && _plot.ActualWidth > 0)
        {
            index = Math.Clamp((int)(position / (_plot.ActualWidth / _samples.Count)), 0, _samples.Count - 1);
        }
        if (index != _hovered)
        {
            _hovered = index;
            Draw();
        }
    }

    private void Draw()
    {
        _plot.Children.Clear();
        _axis.Children.Clear();
        var width = _plot.ActualWidth;
        if (_samples.Count == 0 || width <= 0)
        {
            return;
        }

        var peak = Math.Max(_samples.Max(sample => sample.Tokens) * 1.18, 1);
        var band = width / _samples.Count;
        var points = _samples.Select((sample, index) => new Point(
            (index + 0.5) * band,
            PlotHeight - (sample.Tokens / peak * PlotHeight * _growth.Value))).ToList();

        var area = Curve(points, close: true);
        var fill = new LinearGradientBrush { StartPoint = new Point(0.5, 0), EndPoint = new Point(0.5, 1) };
        fill.GradientStops.Add(new GradientStop { Color = Ui.Color(_tint, 0.42), Offset = 0 });
        fill.GradientStops.Add(new GradientStop { Color = Ui.Color(_tint, 0), Offset = 1 });
        _plot.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = area, Fill = fill });
        _plot.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = Curve(points, close: false),
            Stroke = Ui.Brush(_tint),
            StrokeThickness = 2.5,
            StrokeLineJoin = PenLineJoin.Round,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
        });

        if (_hovered is { } hovered)
        {
            _plot.Children.Add(new Line
            {
                X1 = points[hovered].X,
                X2 = points[hovered].X,
                Y1 = 0,
                Y2 = PlotHeight,
                Stroke = Ui.Tertiary,
                StrokeThickness = 1,
                StrokeDashArray = [2, 2],
            });
        }
        var marked = _hovered ?? _samples.Count - 1;
        var dot = new Ellipse { Width = 11, Height = 11, Fill = Ui.Brush(_tint), Stroke = Ui.Brush(Ui.White, 0.9), StrokeThickness = 2 };
        Canvas.SetLeft(dot, points[marked].X - 5.5);
        Canvas.SetTop(dot, points[marked].Y - 5.5);
        _plot.Children.Add(dot);

        for (var index = 0; index < _samples.Count; index++)
        {
            var isMarked = index == marked;
            var initial = isMarked
                ? Ui.Text(UsageFormat.DayInitial(_samples[index].Date), 11, Ui.Bold, 1, Ui.Ink(Ink.Coral))
                : Ui.Text(UsageFormat.DayInitial(_samples[index].Date), 11, Ui.Medium, Ui.SecondaryOpacity);
            initial.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(initial, index);
            _axis.Children.Add(initial);
        }

        var total = _samples.Sum(sample => sample.Tokens);
        // The title, or once the totals are known, the totals themselves: "Today" and "7 days" say
        // what the curve is, and the title beside them pushed the figures to an ellipsis.
        _hoverHeader.Visibility = _hovered is null ? Visibility.Collapsed : Visibility.Visible;
        _totalsHeader.Visibility = _hovered is null && _today is not null && _week is not null ? Visibility.Visible : Visibility.Collapsed;
        _title.Visibility = _hovered is null && (_today is null || _week is null) ? Visibility.Visible : Visibility.Collapsed;
        if (_hovered is { } day)
        {
            Ui.Restyle(_day, UsageFormat.DayName(_samples[day].Date, DateTimeOffset.UtcNow), Ui.SecondaryOpacity);
            Ui.Restyle(_tokens, UsageFormat.Tokens(_samples[day].Tokens), 1, Ui.Ink(Ink.Coral));
            Ui.Restyle(_share, total > 0 ? $"{Math.Round(_samples[day].Tokens * 100.0 / total).ToString(CultureInfo.CurrentCulture)}% of week" : string.Empty, Ui.SecondaryOpacity);
        }
        else if (_today is { } today && _week is { } week)
        {
            Ui.Restyle(_todayLabel, "Today", Ui.SecondaryOpacity);
            Ui.Restyle(_weekLabel, "7 days", Ui.SecondaryOpacity);
            Ui.Restyle(_todayValue, UsageFormat.Tokens(today), 1, Ui.Ink(Ink.Coral));
            Ui.Restyle(_weekValue, UsageFormat.Tokens(week), 1, Ui.Ink(Ink.Sky));
        }
    }

    /// <summary>A Catmull-Rom curve through the points, as cubic Béziers; closed down to the floor for the area.</summary>
    private static PathGeometry Curve(IReadOnlyList<Point> points, bool close)
    {
        var figure = new PathFigure { StartPoint = close ? new Point(points[0].X, PlotHeight) : points[0], IsClosed = close, IsFilled = close };
        if (close)
        {
            figure.Segments.Add(new LineSegment { Point = points[0] });
        }
        for (var index = 0; index < points.Count - 1; index++)
        {
            var before = points[Math.Max(index - 1, 0)];
            var from = points[index];
            var to = points[index + 1];
            var after = points[Math.Min(index + 2, points.Count - 1)];
            static double Clamp(double y) => Math.Clamp(y, 0, PlotHeight);
            figure.Segments.Add(new BezierSegment
            {
                Point1 = new Point(from.X + ((to.X - before.X) / 6), Clamp(from.Y + ((to.Y - before.Y) / 6))),
                Point2 = new Point(to.X - ((after.X - from.X) / 6), Clamp(to.Y - ((after.Y - from.Y) / 6))),
                Point3 = to,
            });
        }
        if (close)
        {
            figure.Segments.Add(new LineSegment { Point = new Point(points[^1].X, PlotHeight) });
        }
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
