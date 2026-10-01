using Claudio.Core.Design;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>Compact mode: one horizontal strip with the mascot, the lead percentage and its reset time.</summary>
internal sealed partial class MinimalView : StackPanel
{
    private readonly UsageViewModel _model;
    private readonly UpdateChecker _updates;
    private readonly MascotView _mascot = new() { Width = 29, Height = 27 };
    private readonly TextBlock _figure = Ui.Text(string.Empty, 28, Ui.Bold);
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _resetLabel = Ui.Micro(string.Empty, 0.35);
    private readonly TextBlock _resetValue = Ui.Text(string.Empty, 12, Ui.Medium, 0.65);
    private readonly UsageBar _bar = new(3, showsGlow: false);

    public MinimalView(UsageViewModel model, UpdateChecker updates)
    {
        _model = model;
        _updates = updates;
        Width = Theme.Metric("minimalWidth");
        Background = Ui.Primary(0);
        _figure.VerticalAlignment = VerticalAlignment.Center;
        _resetLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _resetValue.HorizontalAlignment = HorizontalAlignment.Right;
        var strip = Ui.Spread(Ui.Row(9, _mascot, _figure, _dots), Ui.Column(0, _resetLabel, _resetValue), minimumGap: 6);
        strip.Margin = new Thickness(14, 9, 14, 8);
        Children.Add(strip);
        Children.Add(_bar);
        Ui.Tooltip(this, "Click for full mode");
        Ui.OnTap(this, model.ToggleMode, "Full mode");
    }

    public void Update()
    {
        var now = DateTimeOffset.UtcNow;
        var snapshot = _model.Snapshot;
        var lead = snapshot.Primary;
        var tint = Theme.Tint(lead.Accent, lead.Percent);

        _mascot.Tint = tint;
        _mascot.IsOverloaded = snapshot.IsOverloaded;
        _mascot.IsWaving = _updates.IsGreeting && !snapshot.IsOverloaded;
        _mascot.IsTyping = snapshot.Session.IsRunning(now);
        Ui.Figure(_figure, UsageFormat.Percent(lead), Ui.Primary(lead.IsMeasured ? 0.95 : 0.45), lead.IsMeasured,
                  13, Ui.Primary(0.4), Ui.Medium);

        _dots.Children.Clear();
        if (_updates.Available is not null)
        {
            var dot = Ui.Dot(5, Theme.Color(Accent.Coral));
            Ui.Tooltip(dot, "A new version of Claudio is available");
            _dots.Children.Add(dot);
        }
        if (_model.ErrorMessage is { } message)
        {
            var dot = Ui.Dot(5, Theme.Danger);
            Ui.Tooltip(dot, message);
            _dots.Children.Add(dot);
        }

        var active = lead.IsActive(now);
        Ui.Restyle(_resetLabel, (active ? "reset" : lead.IsMeasured ? "session" : "quota").ToUpperInvariant(), 0.35);
        Ui.Restyle(_resetValue, active ? UsageFormat.ResetTime(lead.ResetDate, now) : lead.IsMeasured ? "inactive" : "unavailable", 0.65);
        _bar.Update(lead.Percent, tint, active ? lead.Elapsed(now) : null);
    }
}
