using Claudio.Core.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace Claudio.App.Views;

/// <summary>The card's footer: today's sessions, the last update, and a manual refresh.</summary>
internal sealed partial class FooterView : Grid
{
    private readonly TextBlock _sessions = Ui.Text(string.Empty, 9.5, Ui.Medium, 0.4);
    private readonly TextBlock _dot = Ui.Text("·", 9.5, Ui.Medium, 0.25);
    private readonly TextBlock _updated = Ui.Text(string.Empty, 9.5, Ui.Medium, 0.4);
    private readonly FontIcon _icon = Ui.Icon("\uE72C", 9.5, 0.45);
    private readonly RotateTransform _spin = new() { CenterX = 4.75, CenterY = 4.75 };
    private readonly Storyboard _spinning = new();

    public FooterView(Action refresh)
    {
        _icon.RenderTransform = _spin;
        var button = Ui.Plain(_icon, refresh, "Refresh");
        button.Width = 16;
        button.Height = 16;
        var animation = new DoubleAnimation { From = 0, To = 360, Duration = TimeSpan.FromSeconds(0.9), RepeatBehavior = RepeatBehavior.Forever };
        Storyboard.SetTarget(animation, _spin);
        Storyboard.SetTargetProperty(animation, "Angle");
        _spinning.Children.Add(animation);
        var spread = Ui.Spread(Ui.Row(6, _sessions, _dot, _updated), button);
        Children.Add(spread);
    }

    public void Update(int sessions, DateTimeOffset updatedAt, bool isRefreshing)
    {
        Ui.Restyle(_sessions, UsageFormat.Sessions(sessions), 0.4);
        Ui.Restyle(_dot, "·", 0.25);
        Ui.Restyle(_updated, $"updated {UsageFormat.Clock(updatedAt)}", 0.4);
        _icon.Foreground = Ui.Primary(0.45);
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
