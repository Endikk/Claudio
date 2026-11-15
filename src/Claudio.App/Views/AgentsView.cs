using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// The agents in short, for the notification-area flyout and the open island, as Claudy's
/// <c>AgentsShortList</c>: one line each with its Claudy, what it was asked, for how long, and
/// where it is. The tracks and the details stay in the card's Sessions tab, one click away.
/// </summary>
internal sealed partial class AgentsShortList : StackPanel
{
    private readonly AgentsViewModel _model;
    private readonly int _limit;
    private readonly bool _showsElapsed;
    private readonly ContentControl _badge = new() { IsTabStop = false, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _rows = new() { Spacing = 6 };
    private readonly Dictionary<string, AgentShortRow> _cache = [];
    private readonly Button _more;
    private readonly TextBlock _moreLabel = Ui.Text(string.Empty, 11, Ui.SemiBold, Ui.SecondaryOpacity);
    private string _shape = string.Empty;
    private int _badgeCount = -1;

    /// <param name="openWidget">Puts Claudio back as the floating card, which lists every agent.</param>
    /// <param name="showsElapsed">The island is wide enough for the running time too; the flyout keeps the title whole.</param>
    public AgentsShortList(AgentsViewModel model, Action openWidget, int limit = 4, bool showsElapsed = false)
    {
        _model = model;
        _limit = limit;
        _showsElapsed = showsElapsed;
        Spacing = 6;
        var title = Ui.Row(6, Ui.SectionLabel("Agents"), _badge);
        Children.Add(title);
        Children.Add(_rows);
        _more = Ui.Plain(_moreLabel, openWidget, "The widget lists every agent, with its track and the way to stop it");
        _more.HorizontalAlignment = HorizontalAlignment.Left;
        _more.Margin = new Thickness((AgentShortRow.ClaudyHeight * MascotView.AspectRatio) + AgentShortRow.LineSpacing, 0, 0, 0);
        Children.Add(_more);
    }

    public void Update()
    {
        var agents = _model.Agents;
        if (_model.RunningCount != _badgeCount)
        {
            _badgeCount = _model.RunningCount;
            _badge.Content = Ui.CountBadge(_badgeCount, Ink.Coral);
        }
        var shown = agents.Take(_limit).ToList();
        foreach (var gone in _cache.Keys.Except(shown.Select(agent => agent.Id)).ToList())
        {
            _cache.Remove(gone);
        }
        foreach (var agent in shown)
        {
            if (!_cache.TryGetValue(agent.Id, out var row))
            {
                row = new AgentShortRow(_model, _showsElapsed);
                _cache[agent.Id] = row;
            }
            row.Update(agent);
        }
        var shape = string.Join(',', shown.Select(agent => agent.Id));
        if (shape != _shape)
        {
            _shape = shape;
            _rows.Children.Clear();
            foreach (var agent in shown)
            {
                _rows.Children.Add(_cache[agent.Id]);
            }
        }
        _more.Visibility = agents.Count > _limit ? Visibility.Visible : Visibility.Collapsed;
        Ui.Restyle(_moreLabel, $"+{agents.Count - _limit} more · open the widget", Ui.SecondaryOpacity);
    }
}

/// <summary>One line of <see cref="AgentsShortList"/>, as Claudy's <c>AgentShortRow</c>.</summary>
internal sealed partial class AgentShortRow : Grid
{
    /// <summary>Half a unit per sprite pixel: whole device pixels at 200 %, as next to the clock.</summary>
    public const double ClaudyHeight = 13.5;
    public const double LineSpacing = 7;

    private readonly AgentsViewModel _model;
    private readonly bool _showsElapsed;
    private readonly AgentClaudy _claudy;
    private readonly TextBlock _title = Ui.Text(string.Empty, 11, Ui.Medium);
    private readonly TextBlock _elapsed = Ui.Text(string.Empty, 11, Ui.Medium, Ui.SecondaryOpacity);
    private readonly AgentState _state;

    public AgentShortRow(AgentsViewModel model, bool showsElapsed)
    {
        _model = model;
        _showsElapsed = showsElapsed;
        _claudy = new AgentClaudy(model, ClaudyHeight);
        _state = new AgentState(9.5, showsElapsed ? 110 : 96);
        ColumnSpacing = LineSpacing;
        Background = Ui.Primary(0);
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _title.VerticalAlignment = VerticalAlignment.Center;
        _elapsed.VerticalAlignment = VerticalAlignment.Center;
        _elapsed.Visibility = showsElapsed ? Visibility.Visible : Visibility.Collapsed;
        Grid.SetColumn(_title, 1);
        Grid.SetColumn(_elapsed, 2);
        Grid.SetColumn(_state, 3);
        Children.Add(_claudy);
        Children.Add(_title);
        Children.Add(_elapsed);
        Children.Add(_state);
    }

    public void Update(RunningAgent agent)
    {
        var status = agent.Status(_model.Now);
        _claudy.Update(agent, status);
        Ui.Restyle(_title, agent.Title, status == AgentStatus.Done ? 0.5 : 0.82);
        if (_showsElapsed)
        {
            Ui.Restyle(_elapsed, _model.Elapsed(agent), Ui.SecondaryOpacity);
        }
        _state.Update(_model, agent, status);
        Ui.Tooltip(this, _model.Details(agent));
        Ui.Name(this, AgentClaudy.Spoken(agent, status));
    }
}

/// <summary>
/// At rest, in the island's left ear beside Claudy, as Claudy's <c>AgentsEarBadge</c>: how many
/// agents are running, in the colour of the one at work. The ear is 13 units tall: a figure reads
/// there, a second mascot did not.
/// </summary>
internal sealed partial class AgentsEarBadge : Grid
{
    private readonly AgentsViewModel _model;
    private readonly TextBlock _count = Ui.Text(string.Empty, 11, Ui.SemiBold);

    public AgentsEarBadge(AgentsViewModel model)
    {
        _model = model;
        VerticalAlignment = VerticalAlignment.Center;
        Children.Add(_count);
    }

    public void Update()
    {
        var count = _model.RunningCount;
        if (count > 0 && _model.Lead is { } lead)
        {
            Visibility = Visibility.Visible;
            Ui.Restyle(_count, count.ToString(System.Globalization.CultureInfo.CurrentCulture), 1, _model.Tint(lead));
            var spoken = count == 1 ? "1 agent running" : $"{count} agents running";
            Ui.Tooltip(this, spoken);
            Ui.Name(this, spoken);
        }
        else
        {
            Visibility = Visibility.Collapsed;
        }
    }
}
