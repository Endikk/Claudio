using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace Claudio.App.Views;

/// <summary>
/// Seven days of local tokens as bars, today brightest, with the daily average as a dashed line.
/// The bars grow in when the chart appears; hovering one puts its figures in the header.
/// </summary>
internal sealed partial class WeekBarsChart : StackPanel
{
    private const double PlotHeight = 56;

    private readonly TextBlock _title = Ui.Micro("Last 7 days", 0.55);
    private readonly TextBlock _day = Ui.Text(string.Empty, 10.5, Ui.SemiBold, 0.6);
    private readonly TextBlock _tokens = Ui.Text(string.Empty, 10.5, Ui.SemiBold);
    private readonly Canvas _plot = new() { Height = PlotHeight, Background = Ui.Primary(0) };
    private readonly Grid _axis = new();
    private readonly SpringValue _growth;
    private IReadOnlyList<TokenSample> _samples = [];
    private Rgba _tint;
    private int? _hovered;

    public WeekBarsChart()
    {
        Spacing = 7;
        var header = Ui.Spread(_title, Ui.Row(6, _day, _tokens), VerticalAlignment.Bottom, minimumGap: 4);
        header.Height = 12;
        Children.Add(header);
        Children.Add(Ui.Column(4, _plot, _axis));
        _growth = new SpringValue(SpringValue.Gauge, _ => Draw());
        _plot.SizeChanged += (_, _) => Draw();
        _plot.PointerMoved += (_, args) => Hover(args.GetCurrentPoint(_plot).Position.X);
        _plot.PointerExited += (_, _) => Hover(null);
    }

    public void Update(IReadOnlyList<TokenSample> samples, Rgba tint)
    {
        _samples = samples;
        _tint = tint;
        _title.Foreground = Ui.Primary(0.55);
        _axis.ColumnDefinitions.Clear();
        foreach (var _ in samples)
        {
            _axis.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        Draw();
    }

    /// <summary>Opening the flyout replays the bars, as the popover does in Claudy.</summary>
    public void Replay()
    {
        _growth.Set(0, animated: false);
        _growth.Set(1);
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
        var peak = Math.Max(_samples.Max(sample => sample.Tokens) * 1.1, 1);
        var band = width / _samples.Count;
        var average = _samples.Average(sample => (double)sample.Tokens);

        for (var index = 0; index < _samples.Count; index++)
        {
            var height = _samples[index].Tokens / peak * PlotHeight * _growth.Value;
            var opacity = _hovered is { } hovered ? (index == hovered ? 1 : 0.3) : (index == _samples.Count - 1 ? 1 : 0.45);
            var bar = new Rectangle
            {
                Width = band * 0.62,
                Height = Math.Max(height, 0),
                RadiusX = 3,
                RadiusY = 3,
                Fill = Ui.Brush(_tint, opacity),
            };
            Canvas.SetLeft(bar, (index * band) + (band * 0.19));
            Canvas.SetTop(bar, PlotHeight - bar.Height);
            _plot.Children.Add(bar);

            var initial = Ui.Text(UsageFormat.DayInitial(_samples[index].Date), 8.5, Ui.SemiBold, 0.5);
            initial.HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(initial, index);
            _axis.Children.Add(initial);
        }

        if (average > 0)
        {
            var y = PlotHeight - (average / peak * PlotHeight * _growth.Value);
            _plot.Children.Add(new Line { X1 = 0, X2 = width, Y1 = y, Y2 = y, Stroke = Ui.Primary(0.25), StrokeThickness = 1, StrokeDashArray = [3, 3] });
        }

        if (_hovered is { } day)
        {
            Ui.Restyle(_day, UsageFormat.DayName(_samples[day].Date, DateTimeOffset.UtcNow), 0.6);
            Ui.Restyle(_tokens, UsageFormat.Tokens(_samples[day].Tokens), 1, _tint);
            _tokens.FontWeight = Ui.SemiBold;
        }
        else
        {
            _day.Text = string.Empty;
            Ui.Restyle(_tokens, average > 0 ? $"avg {UsageFormat.Tokens((long)average)} / day" : string.Empty, 0.4);
            _tokens.FontWeight = Ui.Medium;
        }
    }
}
