using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Claudio.Core.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace Claudio.App.Views;

/// <summary>
/// The short card that opens above the clock when Claudio lives in the notification area, Claudy's
/// menu bar popover: built around charts rather than the widget's gauges, the quotas as rings with
/// their pace, the agents at work in short, seven days as bars, and the week split by model; a
/// session waiting for the user first. Details, ports and the agents' tracks stay in the widget.
/// </summary>
internal sealed partial class MenuBarView : StackPanel
{
    private readonly UsageViewModel _model;
    private readonly UpdateChecker _updates;
    private readonly MascotView _mascot = new() { Width = 32, Height = 30 };
    private readonly TextBlock _name = Ui.Text("Claudio", 16, Ui.SemiBold);
    private readonly StackPanel _status = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _body = new();
    private readonly QuotaRing _lead = new();
    private readonly QuotaRing _weekly = new(0.08);
    private readonly QuotaRing _scoped = new(0.16);
    private readonly Grid _rings = new() { ColumnSpacing = 4 };
    private readonly WeekBarsChart _bars = new();
    private readonly SignInControls _signIn;
    private readonly StackPanel _signedOut;
    private readonly UpdateRow _updateRow;
    private readonly TextBlock _updated = Ui.Meta(string.Empty);
    private string? _hoveredModel;
    private readonly FontIcon _refresh;
    private readonly TextBlock _widget;

    private readonly AgentsViewModel _agents;
    private readonly WaitingList _waiting;
    private readonly AgentsShortList _agentsList;

    public MenuBarView(UsageViewModel model, UpdateChecker updates, AgentsViewModel agents, Action showWidget)
    {
        _model = model;
        _agents = agents;
        _waiting = new WaitingList(agents, compact: true);
        _agentsList = new AgentsShortList(agents, showWidget);
        _agentsList.Margin = new Thickness(8, 4, 8, 4);
        _updates = updates;
        _signIn = new SignInControls(model);
        // Built once: an element can only ever have one parent, and the body is rebuilt at every reading.
        _signedOut = Ui.Column(10, Ui.Text("Not signed in to Claude", 12, Ui.Medium, Ui.SecondaryOpacity), _signIn);
        _signedOut.Margin = new Thickness(8, 6, 8, 6);
        _updateRow = new UpdateRow(updates);
        var gap = Theme.Metric("gap");
        Spacing = gap;
        _body.Spacing = gap;
        Padding = new Thickness(Theme.Metric("panelPadding"));
        Width = Theme.Metric("menuBarWidth");

        _name.VerticalAlignment = VerticalAlignment.Center;
        var header = Ui.Spread(Ui.Row(8, _mascot, _name), _status);
        header.Margin = new Thickness(4, 0, 4, 0);
        Children.Add(header);
        Children.Add(_waiting);

        for (var column = 0; column < 3; column++)
        {
            _rings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        Grid.SetColumn(_weekly, 1);
        Grid.SetColumn(_scoped, 2);
        _rings.Children.Add(_lead);
        _rings.Children.Add(_weekly);
        _rings.Children.Add(_scoped);
        _rings.Margin = new Thickness(0, 4, 0, 4);
        _bars.Margin = new Thickness(8, 4, 8, 4);
        Children.Add(_body);
        Children.Add(_updateRow);

        // Borderless buttons, in the system's accent as on macOS.
        _refresh = Ui.Icon("\uE72C", 11);
        _widget = Ui.Text("Floating widget", 11, Ui.Medium);
        var refresh = Ui.Plain(_refresh, () => _ = model.RefreshAsync(userInitiated: true), "Refresh");
        var widget = Ui.Plain(_widget, showWidget, "Show the widget on the desktop instead");
        var footer = Ui.Spread(_updated, Ui.Row(10, refresh, widget), minimumGap: 10);
        footer.Margin = new Thickness(6, 0, 6, 0);
        Children.Add(footer);
    }

    /// <summary>Opening the flyout replays the rings and the bars.</summary>
    public void Replay()
    {
        _lead.Replay();
        _weekly.Replay();
        _scoped.Replay();
        _bars.Replay();
    }

    public void Update()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = _model.Snapshot;
        var lead = snapshot.Primary;
        _mascot.Tint = Theme.Tint(lead.Accent, lead.Percent);
        _mascot.IsOverloaded = snapshot.IsOverloaded;
        _mascot.IsTyping = snapshot.Session.IsRunning(now);
        _name.Foreground = Ui.Primary(1);

        _waiting.Update();
        _status.Children.Clear();
        if (_model.ErrorMessage is { } message)
        {
            var dot = Ui.Dot(6, Theme.Danger);
            Ui.Tooltip(dot, message);
            _status.Children.Add(dot);
        }
        if (snapshot.ActiveModel.Length > 0)
        {
            var active = Ui.Text(snapshot.ActiveModel, 12, Ui.Medium, Ui.SecondaryOpacity);
            active.VerticalAlignment = VerticalAlignment.Center;
            _status.Children.Add(active);
        }

        _body.Children.Clear();
        if (_model.IsSignedIn || _model.IsDemo)
        {
            _lead.Update(lead, now);
            _weekly.Update(snapshot.Weekly, now);
            _scoped.Update(snapshot.Scoped, now);
            // A plan billed on usage has no weekly or per-model quota to ring.
            _weekly.Visibility = _scoped.Visibility = snapshot.Spend is null ? Visibility.Visible : Visibility.Collapsed;
            _body.Children.Add(_rings);
            // On the glass, apart by hairlines: only a waiting session is a block.
            if (_agents.Agents.Count > 0)
            {
                _body.Children.Add(Ui.Rule());
                _agentsList.Update();
                _body.Children.Add(_agentsList);
            }
            _body.Children.Add(Ui.Rule());
            _bars.Update(snapshot.History, Theme.Color(Accent.Coral));
            _body.Children.Add(_bars);
            if (snapshot.Models.Count > 0)
            {
                _body.Children.Add(Ui.Rule());
                var split = ModelSplit(snapshot.Models);
                split.Margin = new Thickness(8, 4, 8, 4);
                _body.Children.Add(split);
            }
            // The demo set has no account to sign out of.
            if (_model.IsSignedIn)
            {
                var account = AccountRow(snapshot.Account);
                account.Margin = new Thickness(6, 0, 6, 0);
                _body.Children.Add(account);
            }
        }
        else if (_model.HasLoaded)
        {
            // Before the first reading, a signed-in user would see the way in flash by.
            _signIn.Update();
            _body.Children.Add(_signedOut);
        }
        _body.Visibility = _body.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _updateRow.Update();
        Ui.Restyle(_updated, $"Updated {UsageFormat.Clock(snapshot.UpdatedAt)}", Ui.SecondaryOpacity);
        var accent = Ui.SystemAccent;
        _refresh.Foreground = Ui.Brush(accent, _model.IsRefreshing ? 0.4 : 1);
        _widget.Foreground = Ui.Brush(accent);
    }

    /// <summary>The week's tokens split by model: one stacked bar, then one legend row per model.</summary>
    private StackPanel ModelSplit(IReadOnlyList<ModelUsage> models)
    {
        var bar = new Grid { Height = 8, CornerRadius = new CornerRadius(4) };
        var total = Math.Max(models.Sum(model => model.Share), 0.0001);
        foreach (var (model, index) in models.Select((model, index) => (model, index)))
        {
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(model.Share / total, GridUnitType.Star) });
            var segment = new Rectangle { Fill = Ui.Brush(Theme.Color(model.Accent), ModelOpacity(model.Id)) };
            Grid.SetColumn(segment, index);
            segment.PointerEntered += (_, _) => Hover(model.Id);
            segment.PointerExited += (_, _) => Hover(null);
            bar.Children.Add(segment);
        }

        var legend = Ui.Column(4);
        foreach (var model in models)
        {
            var share = Ui.Text($"{Math.Round(model.Share * 100):0}%", 12, Ui.SemiBold, 1, Theme.Color(model.Accent));
            share.Width = 38;
            share.TextAlignment = TextAlignment.Right;
            var row = Ui.Spread(Ui.Row(6, Ui.Dot(7, Theme.Color(model.Accent)), Ui.Text(model.Name, 13, Ui.Medium)),
                                Ui.Row(8, Ui.Meta(UsageFormat.Tokens(model.Tokens)), share), minimumGap: 8);
            row.Background = Ui.Primary(0);
            row.Opacity = ModelOpacity(model.Id);
            row.PointerEntered += (_, _) => Hover(model.Id);
            row.PointerExited += (_, _) => Hover(null);
            legend.Children.Add(row);
        }
        return Ui.Column(8, Ui.SectionLabel("By model · 7 days"), bar, legend);
    }

    /// <summary>Everything stays lit until something is hovered; then only that model does.</summary>
    private double ModelOpacity(string id) => _hoveredModel is null || _hoveredModel == id ? 1 : 0.3;

    private void Hover(string? id)
    {
        if (_hoveredModel != id)
        {
            _hoveredModel = id;
            Update();
        }
    }

    /// <summary>Whose quotas these are, and the way out. Signing out leaves Claude Code signed in.</summary>
    private Grid AccountRow(Account account)
    {
        var who = Ui.Text(account.Email.Length == 0 ? account.Name : account.Email, 11, Ui.Medium, Ui.SecondaryOpacity);
        var signOut = Ui.Plain(Ui.Text("Sign out", 11, Ui.Medium, 1, Ui.SystemAccent), _model.SignOut,
                               "Claudio stops reading your quotas until you sign back in. Claude Code stays signed in.");
        return Ui.Spread(who, signOut);
    }
}
