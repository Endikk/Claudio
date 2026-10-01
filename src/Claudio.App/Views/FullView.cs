using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App.Views;

/// <summary>
/// Full mode, top to bottom, as Claudy's <c>FullView</c>: header, the usage/ports switch, the 5h
/// session, the weekly and per-model columns, the totals, the sparkline, "Details", and the footer.
/// On a plan billed on usage, the monthly spend takes the session's place and the two columns,
/// which have no quota there, step aside.
/// </summary>
internal sealed partial class FullView : StackPanel
{
    private readonly UsageViewModel _model;
    private readonly UpdateChecker _updates;
    private readonly PortsViewModel _ports;

    private readonly MascotView _mascot = new() { Width = 29, Height = 27 };
    private readonly TextBlock _name = Ui.Text("Claudio", 14, Ui.SemiBold, 0.9);
    private readonly StackPanel _badges = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly AvatarButton _avatar;
    private readonly TabSwitcher _tabs;

    private readonly StackPanel _usage = new() { Spacing = 11 };
    private readonly PortsView _portsView;

    private readonly TextBlock _leadTitle = Ui.Micro(string.Empty, 0.55);
    // SF Rounded semibold is heavier than Nunito's: the bold weight carries the same mass.
    private readonly TextBlock _hero = Ui.Text(string.Empty, 40, Ui.Bold);
    private readonly TextBlock _resetLabel = Ui.Micro(string.Empty, 0.35);
    private readonly TextBlock _resetValue = Ui.Text(string.Empty, 14, Ui.SemiBold, 0.8);
    private readonly TextBlock _countdown = Ui.Text(string.Empty, 9.5, Ui.Medium, 0.35);
    private readonly UsageBar _leadBar = new(8);
    private readonly TextBlock _leadLocal = Ui.Text(string.Empty, 9.5, Ui.Medium, 0.35);
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse _paceDot = new() { Width = 4, Height = 4 };
    private readonly TextBlock _paceText = Ui.Text(string.Empty, 9.5, Ui.Medium);
    private readonly StackPanel _pace;

    private readonly Grid _columns = new() { ColumnSpacing = 9 };
    private readonly StatColumn _weekly = new();
    private readonly StatColumn _scoped = new();

    private readonly TextBlock _todayTitle = Ui.Micro("Today", 0.4);
    private readonly TextBlock _todayValue = Ui.Text(string.Empty, 15, Ui.SemiBold);
    private readonly TextBlock _weekTitle = Ui.Micro("7 days", 0.4);
    private readonly TextBlock _weekValue = Ui.Text(string.Empty, 15, Ui.SemiBold);
    private readonly Border _totalsRule = new() { Width = 1, Height = 26 };

    private readonly SparklineChart _chart = new("Usage · 7 days");
    private readonly DetailsSection _details;
    private readonly UpdateRow _updateRow;
    private readonly FooterView _footer;
    private readonly List<Border> _hairlines = [];

    public FullView(UsageViewModel model, UpdateChecker updates, PortsViewModel ports)
    {
        _model = model;
        _updates = updates;
        _ports = ports;
        Spacing = 11;
        Padding = new Thickness(Theme.Metric("padding"), 14, Theme.Metric("padding"), 14);
        Width = Theme.Metric("fullWidth");

        _avatar = new AvatarButton(model.ToggleProfile);
        _tabs = new TabSwitcher(tab => model.Tab = tab);
        _details = new DetailsSection(model.ToggleDetails);
        _updateRow = new UpdateRow(updates);
        _footer = new FooterView(() => _ = model.RefreshAsync(userInitiated: true));
        _portsView = new PortsView(ports);

        // Header: the mascot, the name, what is going on, and the account.
        var title = Ui.Row(8, _mascot, _name, _badges);
        _name.VerticalAlignment = VerticalAlignment.Center;
        var header = Ui.Spread(title, _avatar);
        header.Background = Ui.Primary(0);
        Ui.OnTap(header, model.ToggleMode, "Minimal mode");
        Children.Add(header);
        Children.Add(_tabs);

        // The lead gauge: the 5h session, or the month's spend.
        var figure = _hero;
        var reset = Ui.Column(1, _resetLabel, _resetValue, _countdown);
        foreach (var line in reset.Children.OfType<FrameworkElement>())
        {
            line.HorizontalAlignment = HorizontalAlignment.Right;
        }
        reset.Margin = new Thickness(0, 12, 0, 0);
        var leadTop = Ui.Spread(Ui.Column(0, _leadTitle, figure), reset, VerticalAlignment.Top);
        _pace = Ui.Row(3, _paceDot, _paceText);
        var leadBottom = Ui.Spread(_leadLocal, _pace);
        var session = Ui.Column(9, leadTop, _leadBar, leadBottom);
        session.Background = Ui.Primary(0);
        Ui.Tooltip(session, "Click for minimal mode");
        Ui.OnTap(session, model.ToggleMode, "Minimal mode");
        _usage.Children.Add(session);

        _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_scoped, 1);
        _columns.Children.Add(_weekly);
        _columns.Children.Add(_scoped);
        _usage.Children.Add(_columns);

        var totals = new Grid();
        totals.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        totals.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        totals.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var today = Total(_todayTitle, _todayValue);
        var week = Total(_weekTitle, _weekValue);
        Grid.SetColumn(_totalsRule, 1);
        Grid.SetColumn(week, 2);
        totals.Children.Add(today);
        totals.Children.Add(_totalsRule);
        totals.Children.Add(week);
        _usage.Children.Add(totals);

        _usage.Children.Add(Hairline());
        _usage.Children.Add(_chart);
        _usage.Children.Add(Hairline());
        _usage.Children.Add(_details);
        _usage.Children.Add(Hairline());
        _usage.Children.Add(_updateRow);
        _usage.Children.Add(_footer);

        Children.Add(_usage);
        Children.Add(_portsView);
    }

    private static StackPanel Total(TextBlock title, TextBlock value)
    {
        title.HorizontalAlignment = HorizontalAlignment.Center;
        value.HorizontalAlignment = HorizontalAlignment.Center;
        return Ui.Column(2, title, value);
    }

    private Border Hairline()
    {
        var line = Ui.Hairline();
        _hairlines.Add(line);
        return line;
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
        _name.Foreground = Ui.Primary(0.9);
        UpdateBadges(snapshot, now);
        _avatar.Update(snapshot.Account, _model.IsProfileVisible);
        _tabs.Update(_model.Tab, _ports.OrphanCount);

        var onUsage = _model.Tab == CardTab.Usage;
        _usage.Visibility = onUsage ? Visibility.Visible : Visibility.Collapsed;
        _portsView.Visibility = onUsage ? Visibility.Collapsed : Visibility.Visible;
        if (!onUsage)
        {
            _portsView.Update();
            return;
        }

        Ui.Restyle(_leadTitle, $"{lead.Title} · {lead.Window}".ToUpperInvariant(), 0.55);
        // The figure fades downward, primary to 72 %, as Claudy's hero gradient.
        var heroInk = new LinearGradientBrush { StartPoint = new Windows.Foundation.Point(0.5, 0), EndPoint = new Windows.Foundation.Point(0.5, 1) };
        heroInk.GradientStops.Add(new GradientStop { Color = Ui.Color(Ui.PrimaryInk, lead.IsMeasured ? 1 : 0.4), Offset = 0.2 });
        heroInk.GradientStops.Add(new GradientStop { Color = Ui.Color(Ui.PrimaryInk, lead.IsMeasured ? 0.72 : 0.28), Offset = 0.9 });
        Ui.Figure(_hero, UsageFormat.Percent(lead), heroInk, lead.IsMeasured, 17, Ui.Primary(0.38), Ui.Medium);

        var active = lead.IsActive(now);
        Ui.Restyle(_resetLabel, (active ? "reset" : lead.IsMeasured ? "session" : "quota").ToUpperInvariant(), 0.35);
        Ui.Restyle(_resetValue, active ? UsageFormat.ResetTime(lead.ResetDate, now) : lead.IsMeasured ? "inactive" : "unavailable", 0.8);
        Ui.Restyle(_countdown, active ? $"in {UsageFormat.Countdown(lead.ResetDate, now)}" : string.Empty, 0.35);
        _countdown.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _leadBar.Update(lead.Percent, tint, active ? lead.Elapsed(now) : null);
        Ui.Restyle(_leadLocal, lead.Amount is { } spend ? UsageFormat.Spent(spend) : $"{UsageFormat.Tokens(lead.TokensUsed)} tokens on this machine", 0.35);
        if (UsageFormat.PaceOf(lead, now) is { } pace)
        {
            _pace.Visibility = Visibility.Visible;
            _paceDot.Fill = Ui.Brush(pace.Color);
            Ui.Restyle(_paceText, pace.Text, 0.9, pace.Color);
        }
        else
        {
            _pace.Visibility = Visibility.Collapsed;
        }

        _columns.Visibility = snapshot.Spend is null ? Visibility.Visible : Visibility.Collapsed;
        _weekly.Update(snapshot.Weekly, now);
        _scoped.Update(snapshot.Scoped, now);

        _todayTitle.Foreground = Ui.Primary(0.4);
        _weekTitle.Foreground = Ui.Primary(0.4);
        Ui.Restyle(_todayValue, UsageFormat.Tokens(snapshot.TodayTokens), 0.95, Theme.Color(Accent.Coral));
        Ui.Restyle(_weekValue, UsageFormat.Tokens(snapshot.WeekTokens), 0.95, Theme.Color(Accent.Sky));
        _totalsRule.Background = Ui.Primary(0.08);
        foreach (var line in _hairlines)
        {
            line.Background = Ui.Primary(0.07);
        }

        _chart.Update(snapshot.History, Theme.Color(Accent.Coral));
        _details.Update(snapshot, _model.IsDetailsExpanded);
        _updateRow.Update();
        _footer.Update(snapshot.SessionCount, snapshot.UpdatedAt, _model.IsRefreshing);
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
            _badges.Children.Add(Ui.Pill(snapshot.ActiveModel, null));
        }
        if (snapshot.QuotaSource.Badge is { } badge)
        {
            var pill = Ui.Pill(badge, Theme.Color(Accent.Amber));
            Ui.Tooltip(pill, UsageFormat.SourceExplanation(snapshot.QuotaSource, now));
            _badges.Children.Add(pill);
        }
        if (_model.ErrorMessage is { } message)
        {
            var pill = Ui.Pill("error", Theme.Danger);
            Ui.Tooltip(pill, message);
            _badges.Children.Add(pill);
        }
    }
}
