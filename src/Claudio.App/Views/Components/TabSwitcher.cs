using Claudio.Core.Design;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

internal enum CardTab
{
    Usage,
    Ports,
}

/// <summary>
/// Two quiet segments in the card header. The badge is the only thing allowed to raise its voice,
/// and only when something is actually left running.
/// </summary>
internal sealed partial class TabSwitcher : Grid
{
    private readonly Dictionary<CardTab, (Border Segment, TextBlock Label, Border Badge, TextBlock Count)> _segments = [];

    public TabSwitcher(Action<CardTab> select)
    {
        var height = Theme.Metric("tabHeight");
        Padding = new Thickness(2);
        // A capsule round the segments: half its own height, never an oval.
        CornerRadius = new CornerRadius((height + 4) / 2);
        HorizontalAlignment = HorizontalAlignment.Left;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var tab in Enum.GetValues<CardTab>())
        {
            var label = Ui.Text(tab.ToString().ToUpperInvariant(), 9.5, Ui.SemiBold);
            label.CharacterSpacing = 63; // 0.6 pt at 9.5 pt
            label.VerticalAlignment = VerticalAlignment.Center;
            var count = Ui.Text(string.Empty, 8.5, Ui.Bold, 1, Theme.Color(Accent.Amber));
            var badge = Ui.Capsule(new Border
            {
                Child = count,
                Padding = new Thickness(4, 1, 4, 1),
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed,
            });
            var segment = new Border
            {
                Child = Ui.Row(4, label, badge),
                Padding = new Thickness(8, 0, 8, 0),
                Height = height,
                CornerRadius = new CornerRadius(height / 2),
            };
            Ui.OnTap(segment, () => select(tab), tab.ToString());
            _segments[tab] = (segment, label, badge, count);
            row.Children.Add(segment);
        }
        Children.Add(row);
    }

    public void Update(CardTab selection, int orphans)
    {
        Background = Ui.Primary(0.06);
        foreach (var (tab, parts) in _segments)
        {
            var selected = tab == selection;
            parts.Segment.Background = selected ? Ui.Primary(0.10) : Ui.Primary(0);
            parts.Label.Foreground = Ui.Primary(selected ? 0.9 : 0.45);
            var showsBadge = tab == CardTab.Ports && orphans > 0;
            parts.Badge.Visibility = showsBadge ? Visibility.Visible : Visibility.Collapsed;
            parts.Badge.Background = Ui.Brush(Theme.Color(Accent.Amber), 0.18);
            parts.Count.Text = orphans.ToString(System.Globalization.CultureInfo.CurrentCulture);
        }
    }
}
