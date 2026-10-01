using System.Globalization;
using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// One open port, as Claudy's <c>PortRow</c>. The kill control stays hidden until the pointer is on
/// the row: an irreversible action has no business being one stray click away at rest. The row is
/// kept and updated in place across scans, so a pointer resting on it keeps its control.
/// </summary>
internal sealed partial class PortRow : Grid
{
    private readonly TextBlock _number = Ui.Text(string.Empty, 13, Ui.SemiBold, 0.95, Theme.Color(Accent.Coral));
    private readonly TextBlock _command = Ui.Text(string.Empty, 11, Ui.Medium, 0.85);
    private readonly TextBlock _subtitle = Ui.Text(string.Empty, 9.5, Ui.Medium, 0.38);
    private readonly Border _orphan = Ui.Pill("orphan", Theme.Color(Accent.Amber), 8.5, stroked: false);
    private readonly TextBlock _failure = Ui.Text(string.Empty, 9.5, Ui.Medium, 0.9, Theme.Danger);
    private readonly Border _kill;
    private readonly ProgressRing _progress = new() { Width = 14, Height = 14, IsActive = true, Visibility = Visibility.Collapsed };
    private bool _isHovered;
    private bool _isKilling;

    public PortRow(Action<PortRow> kill)
    {
        ColumnSpacing = 9;
        Padding = new Thickness(0, 5, 0, 5);
        Background = Ui.Primary(0);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _number.MinWidth = 46;
        _number.VerticalAlignment = VerticalAlignment.Top;
        _orphan.Padding = new Thickness(5, 1, 5, 1);
        _failure.TextWrapping = TextWrapping.Wrap;
        _failure.TextTrimming = TextTrimming.None;
        _failure.MaxLines = 2;
        var details = Ui.Column(2, _command, Ui.Row(5, _subtitle, _orphan), _failure);
        Grid.SetColumn(details, 1);

        _kill = new Border
        {
            Width = 18,
            Height = 18,
            CornerRadius = new CornerRadius(9),
            Background = Ui.Primary(0.08),
            Child = Ui.Icon("\uE711", 8, 0.55),
            VerticalAlignment = VerticalAlignment.Top,
        };
        Ui.OnTap(_kill, () =>
        {
            if (_isHovered && !_isKilling)
            {
                _isKilling = true;
                Refresh();
                kill(this);
            }
        });
        var action = new Grid { Width = 18, VerticalAlignment = VerticalAlignment.Top, Children = { _kill, _progress } };
        Grid.SetColumn(action, 2);

        Children.Add(_number);
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

    public ListeningPort? Port { get; private set; }

    public void Update(ListeningPort port, string? failure)
    {
        Port = port;
        _number.Text = port.Port.ToString(CultureInfo.InvariantCulture);
        Ui.Restyle(_command, port.Command, 0.85);
        Ui.Restyle(_subtitle, PortsText.Subtitle(port, DateTimeOffset.UtcNow), 0.38);
        _orphan.Visibility = port.Attribution == PortAttribution.Orphan ? Visibility.Visible : Visibility.Collapsed;
        _failure.Text = failure ?? string.Empty;
        _failure.Visibility = failure is null ? Visibility.Collapsed : Visibility.Visible;
        if (failure is not null)
        {
            // A refusal ends the attempt: the control comes back, with the reason under the row.
            _isKilling = false;
        }
        Ui.Tooltip(_kill, $"Kill the process on port {port.Port}");
        Ui.Name(_kill, $"Kill the process on port {port.Port}");
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
