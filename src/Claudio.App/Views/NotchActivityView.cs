using Claudio.Core.Design;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Claudio.App.Views;

/// <summary>
/// The open island, as Claudy's <c>NotchActivityView</c>, laid out round the notch like the Dynamic
/// Island round the camera: the mascot on the notch's left and the session figure on its right, each
/// a short flight from its ear; the model at work under the notch; then the session's bar, its pace
/// and reset, and the other quotas across the full width. The island flies the mascot and the figure
/// in: here they only mark where they land.
/// </summary>
internal sealed partial class NotchActivityView : StackPanel
{
    private const double SidePadding = 18;

    /// <summary>Room kept free round the notch in the top row: the notch and its fade.</summary>
    private const double NotchClearance = 16;

    private readonly UsageViewModel _model;
    private readonly Border _mascotLanding = new() { Width = NotchFlight.MascotWidth, Height = NotchFlight.MascotSize, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly IslandPercent _figureLanding = new() { Opacity = 0, IsHitTestVisible = false };
    private readonly TextBlock _leadLabel = Ui.Micro(string.Empty, 0.5);
    private readonly Border _notchSpace = new();
    private readonly Border _modelPill;
    private readonly TextBlock _modelText = Ui.Text(string.Empty, 9.5, Ui.SemiBold, 0.6);
    private readonly Grid _top = new();
    private readonly UsageBar _bar = new(6);
    private readonly TextBlock _pace = Ui.Text(string.Empty, 10.5, Ui.Medium);
    private readonly TextBlock _reset = Ui.Text(string.Empty, 10.5, Ui.Medium, 0.55);
    private readonly StackPanel _window;
    private readonly TextBlock _spent = Ui.Text(string.Empty, 11, Ui.Medium, 0.65);
    private readonly Grid _others = new() { ColumnSpacing = 24 };
    private readonly SignInControls _signIn;
    private readonly StackPanel _signedOut;
    private readonly Grid _error = new();
    private readonly TextBlock _errorText = Ui.Text(string.Empty, 10.5, Ui.Medium, 0.65);
    private readonly UpdateRow _updateRow;
    private readonly List<(Grid Row, TextBlock Title, UsageBar Bar, TextBlock Value)> _small = [];

    public NotchActivityView(UsageViewModel model, UpdateChecker updates)
    {
        _model = model;
        _updateRow = new UpdateRow(updates);
        _signIn = new SignInControls(model);
        // Built once: an element can only ever have one parent, and the body is rebuilt at every reading.
        _signedOut = Ui.Column(8, Ui.Text("Not signed in to Claude", 12, Ui.Medium, 0.7), _signIn);
        Spacing = 10;
        Padding = new Thickness(SidePadding, 6, SidePadding, 16);
        Width = Theme.Metric("islandWidth");

        // Round the notch: the mascot's spot, the model under the notch, the figure's spot.
        _top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _mascotLanding.VerticalAlignment = VerticalAlignment.Center;
        _top.Children.Add(_mascotLanding);

        _modelText.TextTrimming = TextTrimming.None;
        _modelPill = Ui.Capsule(new Border { Padding = new Thickness(7, 3, 7, 3), Child = _modelText, HorizontalAlignment = HorizontalAlignment.Center });
        var underNotch = Ui.Column(0, _notchSpace, _modelPill);
        underNotch.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(underNotch, 1);
        _top.Children.Add(underNotch);

        _figureLanding.HorizontalAlignment = HorizontalAlignment.Right;
        _leadLabel.HorizontalAlignment = HorizontalAlignment.Right;
        var reading = Ui.Column(0, _figureLanding, _leadLabel);
        reading.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(reading, 2);
        _top.Children.Add(reading);
        Children.Add(_top);

        _window = Ui.Column(7, _bar, Ui.Spread(_pace, _reset, minimumGap: 8));
        Children.Add(_window);
        Children.Add(_spent);
        for (var column = 0; column < 2; column++)
        {
            _others.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var small = (Title: Ui.Text(string.Empty, 10.5, Ui.Medium, 0.55), Bar: new UsageBar(4, showsGlow: false), Value: Ui.Text(string.Empty, 11, Ui.SemiBold, 0.85));
            small.Title.TextTrimming = TextTrimming.None;
            small.Value.TextTrimming = TextTrimming.None;
            var row = new Grid { ColumnSpacing = 8 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            small.Title.VerticalAlignment = small.Bar.VerticalAlignment = small.Value.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(small.Bar, 1);
            Grid.SetColumn(small.Value, 2);
            row.Children.Add(small.Title);
            row.Children.Add(small.Bar);
            row.Children.Add(small.Value);
            row.Background = Ui.Primary(0);
            Grid.SetColumn(row, column);
            _others.Children.Add(row);
            _small.Add((row, small.Title, small.Bar, small.Value));
        }
        Children.Add(_others);
        Children.Add(_signedOut);

        var dot = Ui.Dot(5, Theme.Danger);
        dot.VerticalAlignment = VerticalAlignment.Center;
        _errorText.TextWrapping = TextWrapping.Wrap;
        _errorText.MaxLines = 2;
        _error.Children.Add(Ui.Row(6, dot, _errorText));
        Children.Add(_error);
        Children.Add(_updateRow);
    }

    /// <summary>The notch the top row is laid out round.</summary>
    public Size Notch { get; set; }

    /// <summary>Where the mascot lands, its centre in this view's coordinates.</summary>
    public Point MascotLanding => Centre(_mascotLanding);

    /// <summary>Where the figure lands, its centre in this view's coordinates.</summary>
    public Point FigureLanding => Centre(_figureLanding);

    public void Update()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = _model.Snapshot;
        var activity = new NotchActivity(snapshot, now);
        var lead = activity.Lead;
        // No quotas without a Claude session, same rule as the card: the way in instead.
        var showsQuotas = _model.IsSignedIn || snapshot.IsDemo;

        _notchSpace.Height = Notch.Height + 6;
        _top.ColumnDefinitions[1].MinWidth = Notch.Width + (2 * NotchClearance);
        _figureLanding.Update(lead);
        Ui.Restyle(_leadLabel, $"{lead.Title} · {lead.Window}".ToUpperInvariant(), 0.5);
        _modelPill.Visibility = snapshot.ActiveModel.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        Ui.Restyle(_modelText, snapshot.ActiveModel, 0.6);
        _modelPill.Background = Ui.Primary(0.1);

        _window.Visibility = showsQuotas ? Visibility.Visible : Visibility.Collapsed;
        if (showsQuotas)
        {
            var active = lead.IsActive(now);
            _bar.Update(lead.IsMeasured ? lead.Percent : 0, Theme.Tint(lead.Accent, lead.Percent), active ? lead.Elapsed(now) : null);
            if (UsageFormat.PaceOf(lead, now) is { } pace)
            {
                Ui.Restyle(_pace, pace.Text, 1, pace.Color);
            }
            else
            {
                _pace.Text = string.Empty;
            }
            Ui.Restyle(_reset, activity.ResetLine, 0.55);
        }

        _spent.Visibility = showsQuotas && activity.SpentLine is not null ? Visibility.Visible : Visibility.Collapsed;
        Ui.Restyle(_spent, activity.SpentLine ?? string.Empty, 0.65);
        _others.Visibility = showsQuotas && activity.SpentLine is null && activity.Others.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        for (var index = 0; index < _small.Count && index < activity.Others.Count; index++)
        {
            var window = activity.Others[index];
            var (row, title, bar, value) = _small[index];
            Ui.Restyle(title, window.Title, 0.55);
            bar.Update(window.IsMeasured ? window.Percent : 0, Theme.Tint(window.Accent, window.Percent), window.IsActive(now) ? window.Elapsed(now) : null);
            Ui.Restyle(value, window.IsMeasured ? $"{UsageFormat.Percent(window)}%" : UsageFormat.NoFigure, window.IsMeasured ? 0.85 : 0.4);
            Ui.Tooltip(row, window.IsActive(now)
                ? $"{window.Title} · {window.Window}, resets {UsageFormat.ResetTime(window.ResetDate, now)}"
                : $"{window.Title} · {window.Window}");
        }

        _signedOut.Visibility = !showsQuotas && _model.HasLoaded ? Visibility.Visible : Visibility.Collapsed;
        if (!showsQuotas)
        {
            _signIn.Update();
        }

        var error = _model.ErrorMessage;
        _error.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        Ui.Restyle(_errorText, error ?? string.Empty, 0.65);
        _updateRow.Update();
    }

    private Point Centre(FrameworkElement element)
    {
        var origin = element.TransformToVisual(this).TransformPoint(new Point(0, 0));
        return new Point(origin.X + (element.ActualWidth / 2), origin.Y + (element.ActualHeight / 2));
    }
}
