using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App.Views;

/// <summary>The "Details" switch, in the card's footer row, as Claudy's <c>DetailsToggle</c>.</summary>
internal sealed partial class DetailsToggle : Grid
{
    private readonly TextBlock _title = Ui.Text("Details", 12, Ui.SemiBold, Ui.SecondaryOpacity);
    private readonly FontIcon _chevron = Ui.Icon("\uE70D", 10, Ui.SecondaryOpacity);
    private readonly RotateTransform _turn = new() { CenterX = 5, CenterY = 5 };

    public DetailsToggle(Action toggle)
    {
        Height = 30;
        Background = Ui.Primary(0);
        _chevron.RenderTransform = _turn;
        var row = Ui.Row(6, _title, _chevron);
        row.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(row);
        Ui.OnTap(this, toggle, "Details");
    }

    public void Update(bool isExpanded)
    {
        _title.Foreground = Ui.Secondary;
        _chevron.Foreground = Ui.Secondary;
        _turn.Angle = isExpanded ? 0 : -90;
        Ui.Tooltip(this, isExpanded ? "Hide the split by model and project" : "Split by model and project");
    }
}

/// <summary>What "Details" unfolds to, as Claudy's <c>DetailsSection</c>: the week split by model and by project.</summary>
internal sealed partial class DetailsSection : StackPanel
{
    private string? _shown;

    public DetailsSection() => Spacing = 12;

    public void Update(UsageSnapshot snapshot)
    {
        // Rebuilt only when the split moves, so its bars do not grow again at every refresh.
        var shown = string.Join('|', snapshot.Models.Select(model => $"{model.Name}:{model.Tokens}"))
            + string.Join('|', snapshot.Projects.Select(project => $"{project.Name}:{project.Tokens}"))
            + $"|{Ui.IsDark}";
        if (shown == _shown)
        {
            return;
        }
        _shown = shown;
        Children.Clear();
        if (snapshot.Models.Count == 0 && snapshot.Projects.Count == 0)
        {
            var empty = Ui.Meta("No activity recorded over the last 7 days");
            empty.TextWrapping = TextWrapping.Wrap;
            Children.Add(empty);
            return;
        }
        // Shares follow what each token costs, not the raw count: cache reads are most of the
        // volume but weigh a tenth, and Opus weighs five Haiku.
        Children.Add(Ui.Meta("Last 7 days · share weighted by model price"));
        Children.Add(Group("By model", snapshot.Models.Select(model => MiniBarRow(model.Name, model.Tokens, model.Share, Theme.Color(model.Accent)))));
        Children.Add(Group("Top projects", snapshot.Projects.Select(project => MiniBarRow(project.Name, project.Tokens, project.Share, Theme.Color(Accent.Teal)))));
    }

    private static StackPanel Group(string title, IEnumerable<UIElement> rows)
    {
        var group = Ui.Column(8, Ui.SectionLabel(title));
        foreach (var row in rows)
        {
            group.Children.Add(row);
        }
        return group;
    }

    /// <summary>One split row, Claudy's <c>MiniBarRow</c>: name, volume, share, and a thin full-width bar.</summary>
    private static StackPanel MiniBarRow(string name, long tokens, double share, Rgba tint)
    {
        var percent = Ui.Text($"{Math.Round(share * 100):0}%", 12, Ui.SemiBold);
        percent.Width = 40;
        percent.TextAlignment = TextAlignment.Right;
        var figures = Ui.Row(6, Ui.Meta(UsageFormat.Tokens(tokens)), percent);
        var bar = new UsageBar(4);
        bar.Loaded += (_, _) => bar.Update(share, tint);
        return Ui.Column(4, Ui.Spread(Ui.Text(name, 13, Ui.Medium), figures, minimumGap: 4), bar);
    }
}
