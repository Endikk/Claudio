using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// The ports annex, as Claudy's <c>PortsView</c>: what Claude Code left listening, and a way to
/// close it. Below the ports, the MCP servers Claude Code left running: they hold no port, but they
/// hold memory. The empty state is the common case and is written for it: an empty list here is
/// good news, not a failure. Rows are kept across scans and only the list's shape is rebuilt when
/// ports or servers come or go.
/// </summary>
internal sealed partial class PortsView : StackPanel
{
    private readonly PortsViewModel _model;
    private readonly Dictionary<string, ReapableRow> _rows = [];
    /// <summary>The latest reading of each row's process: a kill goes to what the last scan saw.</summary>
    private readonly Dictionary<string, object> _current = [];
    private readonly TextBlock _message = Ui.Text(string.Empty, 12, Ui.Medium, Ui.SecondaryOpacity);
    private readonly TextBlock _degraded = Ui.Text("Process environments unreadable, so orphans cannot be detected", 11, Ui.Medium);
    private readonly StackPanel _ports = new() { Padding = new Thickness(8, 4, 8, 4) };
    private readonly Border _serversRule = Ui.Rule();
    private readonly StackPanel _servers = new() { Padding = new Thickness(8, 4, 8, 4) };
    private readonly TextBlock _memory = Ui.MonoText(string.Empty, 11, Ui.SemiBold);
    private readonly ContentControl _bulk = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, HorizontalAlignment = HorizontalAlignment.Stretch, IsTabStop = false };
    private string _shape = string.Empty;
    private bool _isConfirmingBulkKill;

    public PortsView(PortsViewModel model)
    {
        _model = model;
        Spacing = Theme.Metric("gap");
        _message.Margin = new Thickness(8, 12, 8, 12);
        _message.TextWrapping = TextWrapping.Wrap;
        _degraded.TextWrapping = TextWrapping.Wrap;
        _degraded.Margin = new Thickness(6, 0, 6, 0);
        Children.Add(_message);
        Children.Add(_degraded);
        Children.Add(_ports);
        Children.Add(_serversRule);
        Children.Add(_servers);
        Children.Add(_bulk);
    }

    public void Update()
    {
        var ports = _model.Ports;
        var servers = _model.McpOrphans;
        _degraded.Visibility = _model.State is PortScanState.Ready { IsDegraded: true } ? Visibility.Visible : Visibility.Collapsed;
        Ui.Restyle(_degraded, _degraded.Text, 1, Ui.Ink(Ink.Amber));
        switch (_model.State)
        {
            case PortScanState.Ready { Ports.Count: 0 }:
                ShowMessage("No port left open by Claude");
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

        var ids = ports.Select(port => port.Id).Concat(servers.Select(server => server.Id)).ToList();
        foreach (var gone in _rows.Keys.Except(ids).ToList())
        {
            _rows.Remove(gone);
        }
        _current.Clear();
        foreach (var port in ports)
        {
            _current[port.Id] = port;
        }
        foreach (var server in servers)
        {
            _current[server.Id] = server;
        }
        foreach (var port in ports)
        {
            if (!_rows.TryGetValue(port.Id, out var row))
            {
                var id = port.Id;
                row = PortRow.Make(() =>
                {
                    if (_current.GetValueOrDefault(id) is ListeningPort latest)
                    {
                        _ = _model.KillAsync(latest);
                    }
                });
                _rows[port.Id] = row;
            }
            PortRow.Update(row, port, _model.Failures.GetValueOrDefault(port.Id));
        }
        foreach (var server in servers)
        {
            if (!_rows.TryGetValue(server.Id, out var row))
            {
                var id = server.Id;
                row = McpServerRow.Make(() =>
                {
                    if (_current.GetValueOrDefault(id) is McpOrphan latest)
                    {
                        _ = _model.KillAsync(latest);
                    }
                });
                _rows[server.Id] = row;
            }
            McpServerRow.Update(row, server, _model.Failures.GetValueOrDefault(server.Id));
        }
        Ui.Restyle(_memory, PortsText.Memory(_model.McpMemory), 1, Ui.Ink(Ink.Amber));

        var shape = string.Join(',', ids) + $"|{_model.OrphanCount}|{_isConfirmingBulkKill}|{Ui.IsDark}";
        if (shape != _shape)
        {
            _shape = shape;
            Lay(ports, servers);
        }
        _ports.Visibility = ports.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _serversRule.Visibility = _servers.Visibility = servers.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _bulk.Visibility = _model.OrphanCount > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowMessage(string text)
    {
        Ui.Restyle(_message, text, Ui.SecondaryOpacity);
        _message.Visibility = Visibility.Visible;
    }

    private void Lay(IReadOnlyList<ListeningPort> ports, IReadOnlyList<McpOrphan> servers)
    {
        _ports.Children.Clear();
        if (ports.Count > 0)
        {
            var title = Ui.SectionLabel("Ports opened by Claude");
            title.Margin = new Thickness(0, 0, 0, 6);
            _ports.Children.Add(title);
            AddRows(_ports, ports.Select(port => port.Id));
        }

        _servers.Children.Clear();
        if (servers.Count > 0)
        {
            var header = Ui.Spread(Ui.SectionLabel("MCP servers left behind"), _memory);
            header.Margin = new Thickness(0, 0, 0, 6);
            _servers.Children.Add(header);
            AddRows(_servers, servers.Select(server => server.Id));
        }

        _bulk.Content = _model.OrphanCount > 1 ? (_isConfirmingBulkKill ? Confirmation() : BulkButton()) : null;
    }

    private void AddRows(StackPanel list, IEnumerable<string> ids)
    {
        var first = true;
        foreach (var id in ids)
        {
            if (!first)
            {
                list.Children.Add(new Border { Height = 1, Background = Ui.Primary(0.1) });
            }
            first = false;
            list.Children.Add(_rows[id]);
        }
    }

    private Button BulkButton()
    {
        var button = Ui.PillButton($"Kill {_model.OrphanCount} orphans", () =>
        {
            _isConfirmingBulkKill = true;
            Update();
        }, Theme.Danger, isLarge: true, glyph: "\uEB90");
        button.HorizontalAlignment = HorizontalAlignment.Stretch;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        return button;
    }

    /// <summary>A bulk kill takes several processes down at once: it never fires straight off a click.</summary>
    private StackPanel Confirmation()
    {
        var question = Ui.Text($"Kill {_model.OrphanCount} processes?", 13, Ui.SemiBold);
        // Ports by number, then the MCP servers left behind by name, as Claudy's confirmation.
        var confirm = Ui.PillButton($"Kill {_model.BulkKillTargets}", () =>
        {
            _isConfirmingBulkKill = false;
            _ = _model.KillAllOrphansAsync();
            Update();
        }, Theme.Danger);
        var cancel = QuietButton.Make("Cancel", () =>
        {
            _isConfirmingBulkKill = false;
            Update();
        });
        var buttons = Ui.Row(8, cancel, confirm);
        buttons.HorizontalAlignment = HorizontalAlignment.Right;
        var column = Ui.Column(8, question, buttons);
        column.Margin = new Thickness(6, 0, 6, 0);
        return column;
    }
}
