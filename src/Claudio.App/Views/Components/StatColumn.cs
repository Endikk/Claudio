using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>A secondary column: "Weekly · 7d", "Sonnet · 7d".</summary>
internal sealed partial class StatColumn : Grid
{
    private readonly Microsoft.UI.Xaml.Shapes.Ellipse _dot = new() { Width = 5, Height = 5, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = Ui.Micro(string.Empty, 0.6);
    private readonly TextBlock _figure = Ui.Text(string.Empty, 19, Ui.SemiBold);
    private readonly UsageBar _bar = new(5, showsGlow: false);
    private readonly TextBlock _local = Ui.Text(string.Empty, 9.5, Ui.Medium, 0.38);
    private readonly TextBlock _pace = Ui.Text(string.Empty, 9.5, Ui.SemiBold);

    public StatColumn()
    {
        CornerRadius = new CornerRadius(12);
        Padding = new Thickness(11, 9, 11, 9);
        Children.Add(Ui.Column(6, Ui.Row(4, _dot, _title), _figure, _bar, Ui.Spread(_local, _pace)));
    }

    public void Update(UsageWindow window, DateTimeOffset now)
    {
        Background = Ui.Primary(0.05);
        var tint = Theme.Tint(window.Accent, window.Percent);
        _dot.Fill = Ui.Brush(tint);
        Ui.Restyle(_title, $"{window.Title} · {window.Window}".ToUpperInvariant(), 0.6);
        Ui.Figure(_figure, UsageFormat.Percent(window), Ui.Primary(window.IsMeasured ? 0.92 : 0.4), window.IsMeasured,
                  11, Ui.Primary(0.45), Ui.Medium);
        _bar.Update(window.Percent, tint, window.IsActive(now) ? window.Elapsed(now) : null);
        Ui.Restyle(_local, window.IsMeasured ? UsageFormat.Tokens(window.TokensUsed) + " here" : "no quota", 0.38);
        if (UsageFormat.PaceSign(window, now) is { } sign && UsageFormat.PaceOf(window, now) is { } pace)
        {
            Ui.Restyle(_pace, sign, 1, pace.Color);
        }
        else
        {
            _pace.Text = string.Empty;
        }
    }
}
