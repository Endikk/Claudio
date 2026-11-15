using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// Full mode, top to bottom, as Claudy's <c>FullView</c>: header, tabs, the 5h session in its own
/// glass tile, then on the glass itself the weekly and per-model windows, the week's curve with
/// today's and the week's totals, the details on demand; the footer last. On a plan billed on
/// usage, the monthly spend takes the session's place and the two windows, which have no quota
/// there, step aside.
/// </summary>
internal sealed partial class FullView : StackPanel
{
    private readonly UsageViewModel _model;
    private readonly UpdateChecker _updates;
    private readonly PortsViewModel _ports;
    private readonly Func<(int Agents, int Waiting)> _sessionCounts;

    private readonly MascotView _mascot = new() { Width = 32, Height = 30 };
    private readonly TextBlock _name = Ui.Text("Claudio", 16, Ui.SemiBold);
    private readonly StackPanel _badges = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly AvatarButton _avatar;
    private readonly TabSwitcher _tabs;

    private readonly StackPanel _usage = new();
    private readonly PortsView _portsView;
    private readonly UIElement _sessionsView;
    private readonly Action _updateSessions;

    private readonly Border _sessionTile;
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse _leadDot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _leadTitle = Ui.SectionLabel(string.Empty);
    private readonly Figure _hero = new(44);
    private readonly TextBlock _resetValue = Ui.Text(string.Empty, 13, Ui.SemiBold);
    private readonly TextBlock _countdown = Ui.Meta(string.Empty);
    private readonly UsageBar _leadBar = new(8);
    private readonly TextBlock _leadLocal = Ui.Meta(string.Empty);
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse _paceDot = new() { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _paceText = Ui.Text(string.Empty, 11, Ui.SemiBold);
    private readonly StackPanel _pace;

    private readonly Grid _columns = new() { ColumnSpacing = 14 };
    private readonly Border _columnsRule = Ui.Rule(isVertical: true);
    private readonly Border _columnsHairline = Ui.Rule();
    private readonly StatColumn _weekly = new();
    private readonly StatColumn _scoped = new();

    private readonly SparklineChart _chart = new("Usage · 7 days");
    private readonly Border _detailsHairline = Ui.Rule();
    private readonly DetailsSection _details = new();
    private readonly DetailsToggle _detailsToggle;
    private readonly UpdateRow _updateRow;
    private readonly FooterView _footer;

    /// <param name="sessionsView">The Sessions tab, and how to redraw it.</param>
    /// <param name="sessionCounts">Agents at work and sessions waiting, for the tab's count.</param>
    public FullView(UsageViewModel model, UpdateChecker updates, PortsViewModel ports,
                    UIElement sessionsView, Action updateSessions, Func<(int Agents, int Waiting)> sessionCounts)
    {
        _model = model;
        _updates = updates;
        _ports = ports;
        _sessionsView = sessionsView;
        _updateSessions = updateSessions;
        _sessionCounts = sessionCounts;
        var gap = Theme.Metric("gap");
        Spacing = gap;
        Padding = new Thickness(Theme.Metric("panelPadding"));
        Width = Theme.Metric("fullWidth");
        _usage.Spacing = gap;

        _avatar = new AvatarButton(model.ToggleProfile);
        _tabs = new TabSwitcher(tab => model.Tab = tab);
        _detailsToggle = new DetailsToggle(model.ToggleDetails);
        _updateRow = new UpdateRow(updates);
        _footer = new FooterView(() => _ = model.RefreshAsync(userInitiated: true));
        _portsView = new PortsView(ports);

        // Header: the mascot, the name, what is going on, and the account.
        _name.VerticalAlignment = VerticalAlignment.Center;
        var title = Ui.Row(8, _mascot, _name, _badges);
        var header = Ui.Spread(title, _avatar);
        header.Height = 34;
        header.Margin = new Thickness(4, 0, 4, 0);
        header.Background = Ui.Primary(0);
        Ui.OnTap(header, model.ToggleMode, "Minimal mode");
        Children.Add(header);
        Children.Add(_tabs);

        // One block only, the lead window: the figure people come for. The two weekly windows and
        // the week's curve lie on the glass itself, apart by a hairline, so the card does not read
        // as boxes stacked on boxes.
        var titleRow = Ui.Row(6, _leadDot, _leadTitle);
        var figure = Ui.Column(2, titleRow, _hero);
        _resetValue.HorizontalAlignment = HorizontalAlignment.Right;
        _countdown.HorizontalAlignment = HorizontalAlignment.Right;
        var reset = Ui.Column(2, _resetValue, _countdown);
        reset.Margin = new Thickness(0, 0, 0, 8);
        var top = Ui.Spread(figure, reset, VerticalAlignment.Bottom, minimumGap: 8);
        _pace = Ui.Row(4, _paceDot, _paceText);
        var bottom = Ui.Spread(_leadLocal, _pace, minimumGap: 6);
        _sessionTile = Ui.Tile(Ui.Column(10, top, _leadBar, bottom));
        Ui.Tooltip(_sessionTile, "Click for minimal mode");
        Ui.OnTap(_sessionTile, model.ToggleMode, "Minimal mode");
        _usage.Children.Add(_sessionTile);

        _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _columnsRule.Margin = new Thickness(0);
        Grid.SetColumn(_columnsRule, 1);
        Grid.SetColumn(_scoped, 2);
        _columns.Children.Add(_weekly);
        _columns.Children.Add(_columnsRule);
        _columns.Children.Add(_scoped);
        _columns.Padding = new Thickness(8, 8, 8, 4);
        _usage.Children.Add(_columns);
        _usage.Children.Add(_columnsHairline);

        _chart.Margin = new Thickness(8, 6, 8, 6);
        _usage.Children.Add(_chart);
        _usage.Children.Add(_detailsHairline);
        _details.Margin = new Thickness(8, 6, 8, 6);
        _usage.Children.Add(_details);
        _usage.Children.Add(_updateRow);

        var footerRow = Ui.Spread(_detailsToggle, _footer, minimumGap: 8);
        footerRow.Margin = new Thickness(6, 0, 2, 0);
        _usage.Children.Add(footerRow);

        Children.Add(_usage);
        Children.Add(_portsView);
        Children.Add(_sessionsView);
    }

    public void Update()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = _model.Snapshot;
        var lead = snapshot.Primary;
        var tint = Theme.Tint(lead.Accent, lead.Percent);

        _mascot.IsOverloaded = snapshot.IsOverloaded;
        _mascot.IsWaving = _updates.IsGreeting && !snapshot.IsOverloaded;
        // The header's mascot keeps its coral; only the wave takes the gauge's band.
        _mascot.Tint = _mascot.IsWaving ? tint : Theme.Color(Accent.Coral);
        _mascot.IsTyping = snapshot.Session.IsRunning(now);
        _name.Foreground = Ui.Primary(1);
        UpdateBadges(snapshot, now);
        _avatar.Update(snapshot.Account, _model.IsProfileVisible);
        var (agents, waiting) = _sessionCounts();
        _tabs.Update(_model.Tab, _ports.OrphanCount, agents, waiting);

        _usage.Visibility = _model.Tab == CardTab.Usage ? Visibility.Visible : Visibility.Collapsed;
        _portsView.Visibility = _model.Tab == CardTab.Ports ? Visibility.Visible : Visibility.Collapsed;
        _sessionsView.Visibility = _model.Tab == CardTab.Sessions ? Visibility.Visible : Visibility.Collapsed;
        switch (_model.Tab)
        {
            case CardTab.Ports:
                _portsView.Update();
                return;
            case CardTab.Sessions:
                _updateSessions();
                return;
            default:
                break;
        }

        Ui.RestyleTile(_sessionTile, tint, 0.18);
        _leadDot.Fill = Ui.Brush(tint);
        Ui.Restyle(_leadTitle, $"{lead.Title} · {lead.Window}", Ui.SecondaryOpacity);
        _hero.Update(lead);

        var active = lead.IsActive(now);
        Ui.Restyle(_resetValue, active ? $"Resets {UsageFormat.ResetTime(lead.ResetDate, now)}" : lead.IsMeasured ? "Session inactive" : "Quota unavailable", 1);
        Ui.Restyle(_countdown, active ? $"in {UsageFormat.Countdown(lead.ResetDate, now)}" : string.Empty, Ui.SecondaryOpacity);
        _countdown.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _leadBar.Update(lead.Percent, tint, active ? lead.Elapsed(now) : null);
        Ui.Restyle(_leadLocal, lead.Amount is { } spend ? UsageFormat.Spent(spend) : $"{UsageFormat.Tokens(lead.TokensUsed)} tokens on this machine", Ui.SecondaryOpacity);
        if (UsageFormat.PaceOf(lead, now) is { } pace)
        {
            _pace.Visibility = Visibility.Visible;
            _paceDot.Fill = Ui.Brush(Theme.Color(pace.Ink, isDark: true));
            Ui.Restyle(_paceText, ShortPace(pace.Text), 1, Ui.Ink(pace.Ink));
        }
        else
        {
            _pace.Visibility = Visibility.Collapsed;
        }

        var hasWindows = snapshot.Spend is null;
        _columns.Visibility = hasWindows ? Visibility.Visible : Visibility.Collapsed;
        _columnsHairline.Visibility = _columns.Visibility;
        _weekly.Update(snapshot.Weekly, now);
        _scoped.Update(snapshot.Scoped, now);
        foreach (var rule in new[] { _columnsRule, _columnsHairline, _detailsHairline })
        {
            rule.Background = Ui.Primary(0.1);
        }

        _chart.Update(snapshot.History, Theme.Color(Accent.Coral), snapshot.TodayTokens, snapshot.WeekTokens);
        var expanded = _model.IsDetailsExpanded;
        _detailsHairline.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        _details.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        if (expanded)
        {
            _details.Update(snapshot);
        }
        _detailsToggle.Update(expanded);
        _updateRow.Update();
        _footer.Update(snapshot.SessionCount, snapshot.UpdatedAt, _model.IsRefreshing);
    }

    /// <summary>
    /// "27 pts ahead of pace" next to the token count pushed it to an ellipsis: the tile says
    /// "27 pts ahead", as the weekly columns do.
    /// </summary>
    private static string ShortPace(string text)
    {
        var shorter = text.Replace(" of pace", string.Empty, StringComparison.Ordinal)
                          .Replace("behind pace", "behind", StringComparison.Ordinal);
        return shorter.Length == 0 ? shorter : char.ToUpperInvariant(shorter[0]) + shorter[1..];
    }

    private void UpdateBadges(UsageSnapshot snapshot, DateTimeOffset now)
    {
        _badges.Children.Clear();
        if (_updates.Available is not null)
        {
            var dot = Ui.Dot(6, Theme.Color(Accent.Coral));
            Ui.Tooltip(dot, "A new version of Claudio is available");
            _badges.Children.Add(dot);
        }
        if (snapshot.ActiveModel.Length > 0)
        {
            var model = Ui.Text(snapshot.ActiveModel, 12, Ui.Medium, Ui.SecondaryOpacity);
            model.VerticalAlignment = VerticalAlignment.Center;
            _badges.Children.Add(model);
        }
        if (snapshot.QuotaSource.Badge is { } badge)
        {
            var label = Ui.Text(char.ToUpperInvariant(badge[0]) + badge[1..], 12, Ui.SemiBold, 1, Ui.Ink(Ink.Amber));
            label.VerticalAlignment = VerticalAlignment.Center;
            Ui.Tooltip(label, UsageFormat.SourceExplanation(snapshot.QuotaSource, now));
            _badges.Children.Add(label);
        }
        if (_model.ErrorMessage is { } message)
        {
            var label = Ui.Text("Error", 12, Ui.SemiBold, 1, Ui.Ink(Ink.Danger));
            label.VerticalAlignment = VerticalAlignment.Center;
            Ui.Tooltip(label, message);
            _badges.Children.Add(label);
        }
    }
}
