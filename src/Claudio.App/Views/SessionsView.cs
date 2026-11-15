using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// The Sessions tab, as Claudy's <c>SessionsView</c>: every Claude Code session open on this PC, the
/// ones waiting for the user first, each with the agents it runs underneath, its window one click
/// away and the way to stop it when it went wrong.
///
/// Built like the ports annex. Programs driving Claude Code through the Agent SDK only show while
/// they run agents: nobody sits at them. Rows are kept across scans; only the list's shape is
/// rebuilt when sessions come or go.
/// </summary>
internal sealed partial class SessionsView : StackPanel
{
    private readonly AgentsViewModel _model;
    private readonly TextBlock _empty = Ui.Text("No Claude Code session open", 12, Ui.Medium, Ui.SecondaryOpacity);
    private readonly TextBlock _count = Ui.SectionLabel(string.Empty);
    private readonly AgentTrackLegend _legend = new();
    private readonly Grid _header;
    private readonly StackPanel _waiting;
    private readonly StackPanel _others = new() { Spacing = 2, Padding = new Thickness(8, 4, 8, 4) };
    private readonly TextBlock _notice = Ui.Text(string.Empty, 11, Ui.Medium, Ui.SecondaryOpacity);
    private readonly Dictionary<string, SessionRow> _rows = [];
    private string _shape = string.Empty;

    /// <param name="toggleRoom">Opens or closes the agent room.</param>
    public SessionsView(AgentsViewModel model, Action toggleRoom)
    {
        _model = model;
        var gap = Theme.Metric("gap");
        Spacing = gap;
        _waiting = new StackPanel { Spacing = gap };
        _empty.Margin = new Thickness(8, 12, 8, 12);
        var room = QuietButton.Make("Room", toggleRoom);
        Ui.Tooltip(room, "Opens the agent room: every agent at its desk");
        var trailing = Ui.Row(8, _legend, room);
        _header = Ui.Spread(_count, trailing, minimumGap: 8);
        _header.Margin = new Thickness(6, 0, 6, 0);
        _notice.TextWrapping = TextWrapping.Wrap;
        _notice.Margin = new Thickness(6, 0, 6, 0);
        Children.Add(_empty);
        Children.Add(_header);
        Children.Add(_waiting);
        Children.Add(_others);
        // Outside the list: an interrupt that closed the session empties it at once.
        Children.Add(_notice);
    }

    public void Update()
    {
        var groups = _model.Groups;
        _empty.Visibility = groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _header.Visibility = groups.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        Ui.Restyle(_count, groups.Count == 1 ? "1 session open" : $"{groups.Count} sessions open", Ui.SecondaryOpacity);
        _legend.Visibility = _model.Agents.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var gone in _rows.Keys.Except(groups.Select(group => group.Id)).ToList())
        {
            _rows.Remove(gone);
        }
        foreach (var group in groups)
        {
            if (!_rows.TryGetValue(group.Id, out var row))
            {
                row = new SessionRow(_model);
                _rows[group.Id] = row;
            }
            row.Update(group);
        }

        // A waiting session is its own amber tile, first: it is an alert. The others lie on the glass.
        var waiting = groups.Where(group => group.Session.IsWaiting).ToList();
        var others = groups.Where(group => !group.Session.IsWaiting).ToList();
        var shape = string.Join(',', waiting.Select(group => group.Id)) + "|" + string.Join(',', others.Select(group => group.Id));
        if (shape != _shape)
        {
            _shape = shape;
            _waiting.Children.Clear();
            _others.Children.Clear();
            foreach (var group in waiting)
            {
                _waiting.Children.Add(_rows[group.Id]);
            }
            foreach (var group in others)
            {
                _others.Children.Add(_rows[group.Id]);
            }
        }
        _waiting.Visibility = waiting.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _others.Visibility = others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        _notice.Visibility = _model.Notice is null ? Visibility.Collapsed : Visibility.Visible;
        Ui.Restyle(_notice, _model.Notice ?? string.Empty, Ui.SecondaryOpacity);
    }
}
