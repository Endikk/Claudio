using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>A secondary column, as Claudy's <c>StatColumn</c>: "Weekly · 7d", "Sonnet · 7d".</summary>
internal sealed partial class StatColumn : StackPanel
{
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse _dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = Ui.SectionLabel(string.Empty);
    private readonly Figure _figure = new(26);
    private readonly UsageBar _bar = new(5);
    private readonly TextBlock _local = Ui.Meta(string.Empty);
    private readonly TextBlock _pace = Ui.Text(string.Empty, 11, Ui.SemiBold);

    public StatColumn()
    {
        Spacing = 8;
        Children.Add(Ui.Row(6, _dot, _title));
        Children.Add(_figure);
        Children.Add(_bar);
        Children.Add(Ui.Column(2, _local, _pace));
    }

    public void Update(UsageWindow window, DateTimeOffset now)
    {
        var tint = Theme.Tint(window.Accent, window.Percent);
        _dot.Fill = Ui.Brush(tint);
        Ui.Restyle(_title, $"{window.Title} · {window.Window}", Ui.SecondaryOpacity);
        _figure.Update(window);
        _bar.Update(window.Percent, tint, window.IsActive(now) ? window.Elapsed(now) : null);
        Ui.Restyle(_local, window.IsMeasured ? UsageFormat.Tokens(window.TokensUsed) + " here" : "No quota", Ui.SecondaryOpacity);
        var delta = window.PaceDelta(now);
        if (UsageFormat.PaceOf(window, now) is { } pace && Math.Abs(delta) >= 0.04)
        {
            var points = UsageFormat.PacePoints(delta);
            Ui.Restyle(_pace, delta > 0 ? $"{points} pts ahead" : $"{points} pts behind", 1, Ui.Ink(pace.Ink));
            _pace.Visibility = Visibility.Visible;
        }
        else
        {
            _pace.Visibility = Visibility.Collapsed;
        }
    }
}
