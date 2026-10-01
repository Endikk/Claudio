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
/// Seven days of tokens as a sparkline. No axes and no grid: the shape and the last point are
/// enough. Hovering a day marks it and puts its figures in the header, next to the title.
/// </summary>
internal sealed partial class SparklineChart : StackPanel
{
    private const double PlotHeight = 46;

    private readonly TextBlock _title;
    private readonly TextBlock _day = Ui.Text(string.Empty, 10.5, Ui.SemiBold, 0.6);
    private readonly TextBlock _tokens = Ui.Text(string.Empty, 10.5, Ui.SemiBold);
    private readonly TextBlock _share = Ui.Text(string.Empty, 10.5, Ui.Medium, 0.4);
    private readonly Canvas _plot = new() { Height = PlotHeight, Background = Ui.Primary(0) };
    private readonly Grid _axis = new();
    private readonly SpringValue _growth;
    private IReadOnlyList<TokenSample> _samples = [];
    private Rgba _tint;
    private int? _hovered;

    public SparklineChart(string title)
    {
        Spacing = 7;
        _title = Ui.Micro(title, 0.55);
        var figures = Ui.Row(6, _day, _tokens, _share);
        var header = Ui.Spread(_title, figures, VerticalAlignment.Bottom, minimumGap: 4);
        header.Height = 12;
        Children.Add(header);
        Children.Add(Ui.Column(5, _plot, _axis));
        _growth = new SpringValue(SpringValue.Gauge, _ => Draw(), initial: 0);
        _plot.SizeChanged += (_, _) => Draw();
        _plot.PointerMoved += (_, args) => Hover(args.GetCurrentPoint(_plot).Position.X);
        _plot.PointerExited += (_, _) => Hover(null);
    }

    public void Update(IReadOnlyList<TokenSample> samples, Rgba tint)
    {
        var changed = !samples.Select(sample => sample.Tokens).SequenceEqual(_samples.Select(sample => sample.Tokens));
        _samples = samples;
        _tint = tint;
        _title.Foreground = Ui.Primary(0.55);
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
        fill.GradientStops.Add(new GradientStop { Color = Ui.Color(_tint, 0.38), Offset = 0 });
        fill.GradientStops.Add(new GradientStop { Color = Ui.Color(_tint, 0.02), Offset = 1 });
        _plot.Children.Add(new Microsoft.UI.Xaml.Shapes.Path { Data = area, Fill = fill });
        _plot.Children.Add(new Microsoft.UI.Xaml.Shapes.Path
        {
            Data = Curve(points, close: false),
            Stroke = Ui.Brush(_tint),
            StrokeThickness = 1.8,
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
                Stroke = Ui.Primary(0.25),
                StrokeThickness = 1,
                StrokeDashArray = [2, 2],
            });
        }
        var marked = _hovered ?? _samples.Count - 1;
        var dot = new Ellipse { Width = 7, Height = 7, Fill = Ui.Brush(_tint) };
        Canvas.SetLeft(dot, points[marked].X - 3.5);
        Canvas.SetTop(dot, points[marked].Y - 3.5);
        _plot.Children.Add(dot);

        for (var index = 0; index < _samples.Count; index++)
        {
            var initial = Ui.Text(UsageFormat.DayInitial(_samples[index].Date), 8.5, Ui.SemiBold, index == marked ? 0.65 : 0.28);
            initial.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(initial, index);
            _axis.Children.Add(initial);
        }

        var total = _samples.Sum(sample => sample.Tokens);
        if (_hovered is { } day)
        {
            Ui.Restyle(_day, UsageFormat.DayName(_samples[day].Date, DateTimeOffset.UtcNow), 0.6);
            Ui.Restyle(_tokens, UsageFormat.Tokens(_samples[day].Tokens), 1, _tint);
            Ui.Restyle(_share, total > 0 ? $"{Math.Round(_samples[day].Tokens * 100.0 / total).ToString(CultureInfo.CurrentCulture)} % of week" : string.Empty, 0.4);
        }
        else
        {
            _day.Text = _tokens.Text = _share.Text = string.Empty;
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
