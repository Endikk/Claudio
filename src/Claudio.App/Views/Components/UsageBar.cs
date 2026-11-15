using Claudio.Core.Design;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App.Views;

/// <summary>
/// Claudy's progress bar: a recessed rail, a solid fill, and an optional pace marker. The fill has a visibility floor so a tiny value stays readable, but never at exactly
/// zero: on an empty or unmeasured gauge a coloured dot would read as consumption that does not exist.
/// </summary>
internal sealed partial class UsageBar : Grid
{
    private readonly double _height;
    private readonly Border _rail = new();
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _marker = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TranslateTransform _markerOffset = new();
    private readonly SpringValue _fraction;
    private double? _pace;

    public UsageBar(double height)
    {
        _height = height;
        Height = height;
        _rail.CornerRadius = new CornerRadius(height / 2);
        _fill.CornerRadius = new CornerRadius(height / 2);
        _marker.Width = Math.Max(1.5, height * 0.28);
        _marker.Height = height;
        _marker.CornerRadius = new CornerRadius(_marker.Width / 2);
        _marker.RenderTransform = _markerOffset;
        Children.Add(_rail);
        Children.Add(_fill);
        Children.Add(_marker);
        _fraction = new SpringValue(SpringValue.Gauge, _ => Layout());
        SizeChanged += (_, _) => Layout();
    }

    /// <param name="pace">The share of the window already elapsed, where the marker sits; null on bars that express a share, not a duration.</param>
    public void Update(double percent, Rgba tint, double? pace = null, bool animated = true)
    {
        _pace = pace;
        _rail.Background = Ui.Primary(0.12);
        _fill.Background = Ui.Brush(tint);
        _fraction.Set(Math.Clamp(percent, 0, 1), animated && IsLoaded);
        Layout();
    }

    private void Layout()
    {
        var width = ActualWidth;
        if (width <= 0)
        {
            return;
        }
        var clamped = Math.Clamp(_fraction.Value, 0, 1);
        var fill = clamped <= 0.0005 ? 0 : Math.Max(_height, width * clamped);
        _fill.Width = fill;
        _fill.Visibility = fill > 0 ? Visibility.Visible : Visibility.Collapsed;

        if (_pace is { } pace)
        {
            pace = Math.Clamp(pace, 0, 1);
            // The marker sits on the fill or on the empty rail; without this switch it vanishes
            // against one of the two, in light mode as in dark.
            var onFill = pace <= clamped;
            _marker.Background = onFill ? Ui.Brush(Ui.White, 0.92) : Ui.Primary(0.45);
            _markerOffset.X = (width - _marker.Width) * pace;
            _marker.Visibility = Visibility.Visible;
        }
        else
        {
            _marker.Visibility = Visibility.Collapsed;
        }
    }
}
