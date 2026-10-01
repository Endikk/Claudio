using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace Claudio.App.Views;

/// <summary>
/// The short card that opens above the clock when Claudio lives in the notification area, Claudy's
/// menu bar popover: built around charts rather than the widget's gauges, the quotas as rings with
/// their pace, seven days as bars, and the week split by model. Details and ports stay in the widget.
/// </summary>
internal sealed partial class MenuBarView : StackPanel
{
    private readonly UsageViewModel _model;
    private readonly UpdateChecker _updates;
    private readonly MascotView _mascot = new() { Width = 29, Height = 27 };
    private readonly TextBlock _name = Ui.Text("Claudio", 14, Ui.SemiBold, 0.9);
    private readonly StackPanel _status = new() { Orientation = Orientation.Horizontal, Spacing = 8 };
    private readonly StackPanel _body = new() { Spacing = 12 };
    private readonly QuotaRing _lead = new();
    private readonly QuotaRing _weekly = new(0.08);
    private readonly QuotaRing _scoped = new(0.16);
    private readonly Grid _rings = new() { ColumnSpacing = 4 };
    private readonly WeekBarsChart _bars = new();
    private readonly SignInControls _signIn;
    private readonly StackPanel _signedOut;
    private readonly UpdateRow _updateRow;
    private readonly TextBlock _updated = Ui.Text(string.Empty, 9.5, Ui.Medium, 0.35);
    private string? _hoveredModel;
    private readonly FontIcon _refresh;
    private readonly TextBlock _widget;

    public MenuBarView(UsageViewModel model, UpdateChecker updates, Action showWidget)
    {
        _model = model;
        _updates = updates;
        _signIn = new SignInControls(model);
        // Built once: an element can only ever have one parent, and the body is rebuilt at every reading.
        _signedOut = Ui.Column(10, Ui.Text("Not signed in to Claude", 12, Ui.Medium, 0.7), _signIn);
        _updateRow = new UpdateRow(updates);
        Spacing = 12;
        Padding = new Thickness(Theme.Metric("padding"));
        Width = Theme.Metric("menuBarWidth");

        _name.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(Ui.Spread(Ui.Row(8, _mascot, _name), _status));

        for (var column = 0; column < 3; column++)
        {
            _rings.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }
        Grid.SetColumn(_weekly, 1);
        Grid.SetColumn(_scoped, 2);
        _rings.Children.Add(_lead);
        _rings.Children.Add(_weekly);
        _rings.Children.Add(_scoped);
        Children.Add(_body);
        Children.Add(_updateRow);

        // Borderless buttons, in the system's accent as on macOS.
        _refresh = Ui.Icon("\uE72C", 11);
        _widget = Ui.Text("Floating widget", 11, Ui.Medium);
        var refresh = Ui.Plain(_refresh, () => _ = model.RefreshAsync(userInitiated: true), "Refresh");
        var widget = Ui.Plain(_widget, showWidget, "Show the widget on the desktop instead");
        Children.Add(Ui.Spread(_updated, Ui.Row(10, refresh, widget), minimumGap: 10));
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
        _name.Foreground = Ui.Primary(0.9);

        _status.Children.Clear();
        if (_model.ErrorMessage is { } message)
        {
            var dot = Ui.Dot(6, Theme.Danger);
            Ui.Tooltip(dot, message);
            _status.Children.Add(dot);
        }
        if (snapshot.ActiveModel.Length > 0)
        {
            _status.Children.Add(Ui.Pill(snapshot.ActiveModel, null, stroked: false));
        }

        _body.Children.Clear();
        if (_model.IsSignedIn)
        {
            _lead.Update(lead, now);
            _weekly.Update(snapshot.Weekly, now);
            _scoped.Update(snapshot.Scoped, now);
            // A plan billed on usage has no weekly or per-model quota to ring.
            _weekly.Visibility = _scoped.Visibility = snapshot.Spend is null ? Visibility.Visible : Visibility.Collapsed;
            _body.Children.Add(_rings);
            _body.Children.Add(Ui.Hairline());
            _bars.Update(snapshot.History, Theme.Color(Accent.Coral));
            _body.Children.Add(_bars);
            if (snapshot.Models.Count > 0)
            {
                _body.Children.Add(Ui.Hairline());
                _body.Children.Add(ModelSplit(snapshot.Models));
            }
            _body.Children.Add(Ui.Hairline());
            _body.Children.Add(AccountRow(snapshot.Account));
        }
        else if (_model.HasLoaded)
        {
            // Before the first reading, a signed-in user would see the way in flash by.
            _signIn.Update();
            _body.Children.Add(_signedOut);
        }
        _body.Visibility = _body.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _updateRow.Update();
        Ui.Restyle(_updated, $"updated {UsageFormat.Clock(snapshot.UpdatedAt)}", 0.35);
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
            var share = Ui.Text($"{Math.Round(model.Share * 100):0} %", 10.5, Ui.SemiBold, 0.75);
            share.Width = 34;
            share.TextAlignment = TextAlignment.Right;
            var row = Ui.Spread(Ui.Row(6, Ui.Dot(6, Theme.Color(model.Accent)), Ui.Text(model.Name, 10.5, Ui.Medium, 0.7)),
                                Ui.Row(8, Ui.Text(UsageFormat.Tokens(model.Tokens), 10, Ui.Medium, 0.4), share));
            row.Background = Ui.Primary(0);
            row.Opacity = ModelOpacity(model.Id);
            row.PointerEntered += (_, _) => Hover(model.Id);
            row.PointerExited += (_, _) => Hover(null);
            legend.Children.Add(row);
        }
        return Ui.Column(7, Ui.Micro("By model · 7d", 0.55), bar, legend);
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
        var who = Ui.Text(account.Email.Length == 0 ? account.Name : account.Email, 10.5, Ui.Medium, 0.55);
        var signOut = Ui.Plain(Ui.Text("Sign out", 11, Ui.Medium, 1, Ui.SystemAccent), _model.SignOut,
                               "Claudio stops reading your quotas until you sign back in. Claude Code stays signed in.");
        return Ui.Spread(who, signOut);
    }
}
