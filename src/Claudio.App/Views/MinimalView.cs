using Claudio.Core.Design;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// Compact mode, as Claudy's <c>MinimalView</c>: one strip, the mark, the 5h window percentage and
/// the reset time, the gauge underneath.
/// </summary>
internal sealed partial class MinimalView : StackPanel
{
    private readonly UsageViewModel _model;
    private readonly UpdateChecker _updates;
    private readonly MascotView _mascot = new() { Width = 30, Height = 28 };
    private readonly Figure _figure = new(28);
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _resetLabel = Ui.Meta(string.Empty);
    private readonly TextBlock _resetValue = Ui.Text(string.Empty, 13, Ui.SemiBold);
    private readonly UsageBar _bar = new(5);

    public MinimalView(UsageViewModel model, UpdateChecker updates)
    {
        _model = model;
        _updates = updates;
        Width = Theme.Metric("minimalWidth");
        Background = Ui.Primary(0);
        _figure.VerticalAlignment = VerticalAlignment.Center;
        _resetLabel.HorizontalAlignment = HorizontalAlignment.Right;
        _resetValue.HorizontalAlignment = HorizontalAlignment.Right;
        Spacing = 8;
        Padding = new Thickness(14, 10, 14, 10);
        var strip = Ui.Spread(Ui.Row(10, _mascot, _figure, _dots), Ui.Column(1, _resetLabel, _resetValue), minimumGap: 6);
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
        _figure.Update(lead);

        _dots.Children.Clear();
        if (_updates.Available is not null)
        {
            var dot = Ui.Dot(5, Theme.Color(Accent.Coral));
            Ui.Tooltip(dot, "A new version of Claudio is available");
            _dots.Children.Add(dot);
        }
        if (_model.ErrorMessage is { } message)
        {
            var dot = Ui.Dot(6, Theme.Danger);
            Ui.Tooltip(dot, message);
            _dots.Children.Add(dot);
        }

        var active = lead.IsActive(now);
        Ui.Restyle(_resetLabel, active ? "Resets" : lead.IsMeasured ? "Session" : "Quota", Ui.SecondaryOpacity);
        Ui.Restyle(_resetValue, active ? UsageFormat.ResetTime(lead.ResetDate, now) : lead.IsMeasured ? "Inactive" : "Unavailable", 1);
        _bar.Update(lead.Percent, tint, active ? lead.Elapsed(now) : null);
    }
}
