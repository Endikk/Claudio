using Claudio.Core.Design;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App.Views;

/// <summary>
/// Claudy's progress bar: a recessed rail, a gradient fill with a halo of its tint, and an optional
/// pace marker. The fill has a visibility floor so a tiny value stays readable, but never at exactly
/// zero: on an empty or unmeasured gauge a coloured dot would read as consumption that does not exist.
/// </summary>
internal sealed partial class UsageBar : Grid
{
    private const double GlowBlur = 5;
    private const double GlowOffset = 1;

    private readonly double _height;
    private readonly bool _showsGlow;
    private readonly Border _rail = new();
    private readonly Border _fill = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Border _marker = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TranslateTransform _markerOffset = new();
    private readonly SpringValue _fraction;
    private Image? _glow;
    private (Rgba Tint, double Scale) _glowKey;
    private Rgba _tint;
    private double? _pace;

    public UsageBar(double height, bool showsGlow = true)
    {
        _height = height;
        _showsGlow = showsGlow;
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
        _tint = tint;
        _rail.Background = Ui.Primary(0.09);
        var gradient = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0, 0.5), EndPoint = new Windows.Foundation.Point(1, 0.5) };
        gradient.GradientStops.Add(new GradientStop { Color = Ui.Color(tint, 0.72), Offset = 0 });
        gradient.GradientStops.Add(new GradientStop { Color = Ui.Color(tint), Offset = 1 });
        _fill.Background = gradient;
        if (_showsGlow && _glowKey != (tint, Ui.Scale))
        {
            // The halo of the tint under the fill, Claudy's `.shadow(color: tint.opacity(0.45), radius: 5, y: 1)`.
            _glowKey = (tint, Ui.Scale);
            if (_glow is not null)
            {
                Children.Remove(_glow);
            }
            _glow = OutlineShadow.Stretchable(_height, tint with { A = (byte)Math.Round(255 * 0.45) }, GlowBlur, GlowOffset, Ui.Scale);
            Children.Insert(1, _glow);
        }
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
        if (_glow is not null)
        {
            _glow.Width = fill + (2 * OutlineShadow.Margin(GlowBlur, GlowOffset));
            _glow.Visibility = fill > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

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
