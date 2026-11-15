using System.Globalization;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// A process the Ports tab can kill, as Claudy's <c>ReapableRow</c>. The kill control stays hidden
/// until the pointer is on the row: an irreversible action has no business being one stray click
/// away at rest. The row is kept and updated in place across scans, so a pointer resting on it
/// keeps its control.
/// </summary>
internal sealed partial class ReapableRow : Grid
{
    /// <summary>Wide enough for "412 MB", so the ports and the servers below them share one column.</summary>
    private const double LeadWidth = 60;

    private readonly TextBlock _lead;
    private readonly TextBlock _title = Ui.MonoText(string.Empty, 13, Ui.Regular);
    private readonly TextBlock _subtitle = Ui.Text(string.Empty, 11, Ui.Medium, Ui.SecondaryOpacity);
    private readonly TextBlock _orphan = Ui.Text("Orphan", 11, Ui.SemiBold);
    private readonly TextBlock _failure = Ui.Text(string.Empty, 11, Ui.Medium, 0.9, Theme.Danger);
    private readonly Border _kill;
    private readonly ProgressRing _progress = new() { Width = 16, Height = 16, IsActive = true, Visibility = Visibility.Collapsed };
    private bool _isHovered;
    private bool _isKilling;

    public ReapableRow(double leadSize, Action kill)
    {
        _lead = Ui.MonoText(string.Empty, leadSize, Ui.SemiBold);
        ColumnSpacing = 9;
        Padding = new Thickness(0, 5, 0, 5);
        Background = Ui.Primary(0);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _lead.MinWidth = LeadWidth;
        _lead.TextTrimming = TextTrimming.None;
        _lead.VerticalAlignment = VerticalAlignment.Top;
        _failure.TextWrapping = TextWrapping.Wrap;
        _failure.MaxLines = 2;
        var details = Ui.Column(2, _title, Ui.Row(5, _subtitle, _orphan), _failure);
        Grid.SetColumn(details, 1);

        _kill = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Child = Ui.Icon("\uE711", 9, Ui.SecondaryOpacity),
            VerticalAlignment = VerticalAlignment.Top,
        };
        Ui.OnTap(_kill, () =>
        {
            if (_isHovered && !_isKilling)
            {
                _isKilling = true;
                Refresh();
                kill();
            }
        });
        var action = new Grid { Width = 18, VerticalAlignment = VerticalAlignment.Top, Children = { _kill, _progress } };
        Grid.SetColumn(action, 2);

        Children.Add(_lead);
        Children.Add(details);
        Children.Add(action);
        PointerEntered += (_, _) =>
        {
            _isHovered = true;
            Refresh();
        };
        PointerExited += (_, _) =>
        {
            _isHovered = false;
            Refresh();
        };
    }

    public void Update(string lead, string title, string subtitle, bool isOrphan, string? failure, string killLabel)
    {
        Ui.Restyle(_lead, lead, 1, Ui.Ink(isOrphan ? Ink.Amber : Ink.Sky));
        Ui.Restyle(_title, title, 1);
        Ui.Restyle(_subtitle, subtitle, Ui.SecondaryOpacity);
        Ui.Restyle(_orphan, "Orphan", 1, Ui.Ink(Ink.Amber));
        _orphan.Visibility = isOrphan ? Visibility.Visible : Visibility.Collapsed;
        _failure.Text = failure ?? string.Empty;
        _failure.Visibility = failure is null ? Visibility.Collapsed : Visibility.Visible;
        if (failure is not null)
        {
            // A refusal ends the attempt: the control comes back, with the reason under the row.
            _isKilling = false;
        }
        Ui.Tooltip(_kill, killLabel);
        Ui.Name(_kill, killLabel);
        Refresh();
    }

    private void Refresh()
    {
        _kill.Background = Ui.Primary(0.08);
        _kill.Opacity = _isHovered ? 1 : 0;
        _kill.Visibility = _isKilling ? Visibility.Collapsed : Visibility.Visible;
        _progress.Visibility = _isKilling ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>One open port, as Claudy's <c>PortRow</c>.</summary>
internal static class PortRow
{
    public static ReapableRow Make(Action kill) => new(14, kill);

    public static void Update(ReapableRow row, ListeningPort port, string? failure) =>
        row.Update(port.Port.ToString(CultureInfo.InvariantCulture), port.Command, PortsText.Subtitle(port, DateTimeOffset.UtcNow),
                   port.Attribution == PortAttribution.Orphan, failure, $"Kill the process on port {port.Port}");
}

/// <summary>
/// One MCP server left behind, as Claudy's <c>McpServerRow</c>. Its weight leads where a port shows
/// its number: the memory is the reason to kill it.
/// </summary>
internal static class McpServerRow
{
    public static ReapableRow Make(Action kill) => new(12, kill);

    public static void Update(ReapableRow row, McpOrphan server, string? failure) =>
        row.Update(PortsText.Memory(server.ResidentBytes), server.Name, PortsText.Subtitle(server, DateTimeOffset.UtcNow),
                   isOrphan: true, failure, PortsText.KillLabel(server));
}
