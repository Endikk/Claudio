using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// The ports annex: what Claude Code left listening, and a way to close it. The empty state is the
/// common case and is written for it: an empty list here is good news, not a failure. Rows are
/// kept across scans and only the list's shape is rebuilt when ports come or go.
/// </summary>
internal sealed partial class PortsView : StackPanel
{
    private readonly PortsViewModel _model;
    private readonly Dictionary<string, PortRow> _rows = [];
    private readonly TextBlock _message = Ui.Text(string.Empty, 11, Ui.Medium, 0.4);
    private readonly TextBlock _degraded = Ui.Text("Process environments unreadable, so orphans cannot be detected.", 9.5, Ui.Medium, 0.9, Theme.Color(Accent.Amber));
    private readonly StackPanel _list = new();
    private readonly ContentControl _bulk = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private string _shape = string.Empty;
    private bool _isConfirmingBulkKill;

    public PortsView(PortsViewModel model)
    {
        _model = model;
        Spacing = 8;
        _message.Margin = new Thickness(0, 18, 0, 18);
        _message.TextWrapping = TextWrapping.Wrap;
        _message.TextTrimming = TextTrimming.None;
        _degraded.TextWrapping = TextWrapping.Wrap;
        _degraded.TextTrimming = TextTrimming.None;
        _bulk.Margin = new Thickness(0, 8, 0, 0);
        Children.Add(_message);
        Children.Add(_degraded);
        Children.Add(_list);
    }

    public void Update()
    {
        var ports = _model.State is PortScanState.Ready ready ? ready.Ports : [];
        _degraded.Visibility = _model.State is PortScanState.Ready { IsDegraded: true } ? Visibility.Visible : Visibility.Collapsed;
        switch (_model.State)
        {
            case PortScanState.Ready { Ports.Count: 0 }:
                ShowMessage("No port left open by Claude.");
                break;
            case PortScanState.Ready:
                _message.Visibility = Visibility.Collapsed;
                break;
            case PortScanState.Unavailable unavailable:
                ShowMessage($"Scan unavailable: {unavailable.Reason}");
                break;
            default:
                ShowMessage("Scanning ports…");
                break;
        }

        foreach (var gone in _rows.Keys.Except(ports.Select(port => port.Id)).ToList())
        {
            _rows.Remove(gone);
        }
        foreach (var port in ports)
        {
            if (!_rows.TryGetValue(port.Id, out var row))
            {
                row = new PortRow(Kill);
                _rows[port.Id] = row;
            }
            row.Update(port, _model.Failures.TryGetValue(port.Id, out var failure) ? failure : null);
        }

        var orphans = _model.Orphans;
        var shape = string.Join(',', ports.Select(port => port.Id)) + $"|{orphans.Count}|{_isConfirmingBulkKill}|{Ui.IsDark}";
        if (shape != _shape)
        {
            _shape = shape;
            Lay(ports, orphans);
        }
        _list.Visibility = ports.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessage(string text)
    {
        Ui.Restyle(_message, text, 0.4);
        _message.Visibility = Visibility.Visible;
    }

    private void Lay(IReadOnlyList<ListeningPort> ports, IReadOnlyList<ListeningPort> orphans)
    {
        _list.Children.Clear();
        if (ports.Count == 0)
        {
            return;
        }
        var title = Ui.Micro("Ports opened by Claude", 0.55);
        title.Margin = new Thickness(0, 0, 0, 4);
        _list.Children.Add(title);
        for (var index = 0; index < ports.Count; index++)
        {
            if (index > 0)
            {
                _list.Children.Add(Ui.Hairline(0.06));
            }
            _list.Children.Add(_rows[ports[index].Id]);
        }
        if (orphans.Count > 1)
        {
            _bulk.Content = _isConfirmingBulkKill ? Confirmation(orphans) : BulkButton(orphans.Count);
            _list.Children.Add(_bulk);
        }
    }

    private Button BulkButton(int count) => Ui.Plain(Ui.Text($"Kill {count} orphans", 9.5, Ui.SemiBold, 0.9, Theme.Danger), () =>
    {
        _isConfirmingBulkKill = true;
        Update();
    });

    /// <summary>A bulk kill takes several processes down at once: it never fires straight off a click.</summary>
    private StackPanel Confirmation(IReadOnlyList<ListeningPort> orphans)
    {
        var numbers = string.Join(", ", orphans.Select(port => port.Port));
        var confirm = Ui.Plain(Ui.Text($"Kill {numbers}", 9.5, Ui.SemiBold, 0.9, Theme.Danger), () =>
        {
            _isConfirmingBulkKill = false;
            _ = _model.KillAllOrphansAsync();
            Update();
        });
        var cancel = Ui.Plain(Ui.Text("Cancel", 9.5, Ui.Medium, 0.5), () =>
        {
            _isConfirmingBulkKill = false;
            Update();
        });
        return Ui.Column(4, Ui.Text($"Kill {orphans.Count} processes?", 10, Ui.SemiBold, 0.8), Ui.Row(12, confirm, cancel));
    }

    private void Kill(PortRow row)
    {
        if (row.Port is { } port)
        {
            _ = _model.KillAsync(port);
        }
    }
}
