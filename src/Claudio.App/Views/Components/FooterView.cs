using Claudio.Core.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Claudio.App.Views;

/// <summary>The card's footer, as Claudy's <c>FooterView</c>: session count, last update time, manual refresh.</summary>
internal sealed partial class FooterView : StackPanel
{
    private readonly TextBlock _line = Ui.Meta(string.Empty);
    private readonly Button _button;
    private readonly FontIcon _icon;
    private readonly RotateTransform _spin = new() { CenterX = 5.5, CenterY = 5.5 };
    private readonly Storyboard _spinning = new();

    public FooterView(Action refresh)
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        VerticalAlignment = VerticalAlignment.Center;
        (_button, _icon) = Ui.RoundButton("\uE72C", refresh, "Refresh");
        _icon.RenderTransform = _spin;
        var animation = new DoubleAnimation { From = 0, To = 360, Duration = TimeSpan.FromSeconds(0.9), RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(animation, _spin);
        Storyboard.SetTargetProperty(animation, "Angle");
        _spinning.Children.Add(animation);
        _line.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_line);
        Children.Add(_button);
    }

    public void Update(int sessions, DateTimeOffset updatedAt, bool isRefreshing)
    {
        var clock = UsageFormat.Clock(updatedAt);
        Ui.Restyle(_line, $"{sessions} session{(sessions > 1 ? "s" : string.Empty)} · {clock}", Ui.SecondaryOpacity);
        _icon.Foreground = Ui.Secondary;
        if (_button.Content is Border disc)
        {
            disc.Background = Ui.Primary(0.08);
        }
        Ui.Tooltip(_button, $"Refresh, last updated {clock}");
        if (isRefreshing)
        {
            _spinning.Begin();
        }
        else
        {
            _spinning.Stop();
        }
    }
}
