using Claudio.Core.Design;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App.Views;

internal enum CardTab
{
    Usage,
    Ports,
    Sessions,
}

/// <summary>
/// The card's tabs, as Claudy's <c>TabSwitcher</c>: a capsule of glass, the chosen tab a brighter
/// lens between them. The counts are the only things allowed to raise their voice, and only when
/// something is actually happening: ports left open and sessions waiting for the user in amber,
/// agents at work in coral.
/// </summary>
internal sealed partial class TabSwitcher : Grid
{
    private readonly Dictionary<CardTab, (Border Segment, TextBlock Label, ContentControl Badge)> _segments = [];

    public TabSwitcher(Action<CardTab> select)
    {
        var height = Theme.Metric("tabHeight");
        Padding = new Thickness(3);
        // A capsule round the segments: half its own height, never an oval.
        CornerRadius = new CornerRadius((height + 6) / 2);
        foreach (var tab in Enum.GetValues<CardTab>())
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var label = Ui.Text(tab.ToString(), 12, Ui.SemiBold);
            label.VerticalAlignment = VerticalAlignment.Center;
            var badge = new ContentControl { VerticalAlignment = VerticalAlignment.Center, IsTabStop = false };
            var row = Ui.Row(6, label, badge);
            row.HorizontalAlignment = HorizontalAlignment.Center;
            var segment = new Border
            {
                Child = row,
                Height = height,
                CornerRadius = new CornerRadius(height / 2),
                BorderThickness = new Thickness(0.5),
            };
            Ui.OnTap(segment, () => select(tab), tab.ToString());
            Grid.SetColumn(segment, (int)tab);
            _segments[tab] = (segment, label, badge);
            Children.Add(segment);
        }
    }

    /// <param name="orphans">Ports and MCP servers left behind.</param>
    /// <param name="agents">Agents at work.</param>
    /// <param name="waiting">Sessions waiting for the user.</param>
    public void Update(CardTab selection, int orphans, int agents = 0, int waiting = 0)
    {
        Background = Ui.Primary(0.06);
        foreach (var (tab, parts) in _segments)
        {
            var selected = tab == selection;
            parts.Segment.Background = selected ? Ui.Brush(Ui.White, Ui.IsDark ? 0.16 : 0.85) : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            parts.Segment.BorderBrush = selected ? Ui.Brush(Ui.White, Ui.IsDark ? 0.2 : 0.6) : null;
            parts.Label.Foreground = selected ? Ui.Primary(1) : Ui.Secondary;
            Ui.Name(parts.Segment, selected ? $"{tab}, selected" : tab.ToString());

            Border? badge = null;
            string? help = null;
            if (tab == CardTab.Ports && orphans > 0)
            {
                badge = Ui.CountBadge(orphans, Ink.Amber);
                help = orphans == 1 ? "1 orphan to clean up" : $"{orphans} orphans to clean up";
            }
            else if (tab == CardTab.Sessions && waiting > 0)
            {
                badge = Ui.CountBadge(waiting, Ink.Amber, showsHand: true);
                help = waiting == 1 ? "1 session waits for you" : $"{waiting} sessions wait for you";
            }
            else if (tab == CardTab.Sessions && agents > 0)
            {
                badge = Ui.CountBadge(agents, Ink.Coral);
                help = agents == 1 ? "1 agent running" : $"{agents} agents running";
            }
            parts.Badge.Content = badge;
            parts.Badge.Visibility = badge is null ? Visibility.Collapsed : Visibility.Visible;
            Ui.Tooltip(parts.Badge, help);
        }
    }
}
