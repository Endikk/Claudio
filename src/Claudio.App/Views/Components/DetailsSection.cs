using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App.Views;

/// <summary>The "Details" accordion: the week split by model and the top projects.</summary>
internal sealed partial class DetailsSection : StackPanel
{
    private readonly TextBlock _title = Ui.Micro("Details", 0.6);
    private readonly FontIcon _chevron = Ui.Icon("\uE70D", 8, 0.4);
    private readonly RotateTransform _turn = new() { CenterX = 4, CenterY = 4 };
    private readonly StackPanel _content = new() { Spacing = 12 };
    private string? _shown;

    public DetailsSection(Action toggle)
    {
        Spacing = 10;
        _chevron.RenderTransform = _turn;
        var header = Ui.Row(6, _title, _chevron);
        var hit = new Grid { Background = Ui.Primary(0), Children = { header } };
        Ui.OnTap(hit, toggle, "Details");
        Children.Add(hit);
        Children.Add(_content);
    }

    public void Update(UsageSnapshot snapshot, bool isExpanded)
    {
        _title.Foreground = Ui.Primary(0.6);
        _chevron.Foreground = Ui.Primary(0.4);
        _turn.Angle = isExpanded ? 0 : -90;
        // Rebuilt only when the split moves, so its bars do not grow again at every refresh.
        var shown = string.Join('|', snapshot.Models.Select(model => $"{model.Name}:{model.Tokens}"))
            + string.Join('|', snapshot.Projects.Select(project => $"{project.Name}:{project.Tokens}"))
            + $"|{isExpanded}|{Ui.IsDark}";
        if (shown == _shown)
        {
            return;
        }
        _shown = shown;
        _content.Children.Clear();
        _content.Visibility = isExpanded ? Visibility.Visible : Visibility.Collapsed;
        if (!isExpanded)
        {
            return;
        }
        if (snapshot.Models.Count == 0 && snapshot.Projects.Count == 0)
        {
            var empty = Ui.Text("No activity recorded over the last 7 days.", 10.5, Ui.Regular, 0.4);
            empty.TextWrapping = TextWrapping.Wrap;
            _content.Children.Add(empty);
            return;
        }
        // Shares follow what each token costs, not the raw count: cache reads are most of the
        // volume but weigh a tenth, and Opus weighs five Haiku.
        _content.Children.Add(Ui.Text("Last 7 days · share weighted by model price", 9, Ui.Regular, 0.35));
        _content.Children.Add(Group("By model", snapshot.Models.Select(model => Row(model.Name, model.Tokens, model.Share, Theme.Color(model.Accent)))));
        _content.Children.Add(Group("Top projects", snapshot.Projects.Select(project => Row(project.Name, project.Tokens, project.Share, Theme.Color(Accent.Sage)))));
    }

    private static StackPanel Group(string title, IEnumerable<UIElement> rows)
    {
        var group = Ui.Column(7, Ui.Text(title, 9, Ui.SemiBold, 0.35));
        foreach (var row in rows)
        {
            group.Children.Add(row);
        }
        return group;
    }

    /// <summary>One split row: name, volume, share, and a thin full-width bar.</summary>
    private static StackPanel Row(string name, long tokens, double share, Rgba tint)
    {
        var percent = Ui.Text($"{Math.Round(share * 100):0} %", 10.5, Ui.SemiBold, 1, tint);
        percent.Width = 36;
        percent.TextAlignment = TextAlignment.Right;
        var figures = Ui.Row(6, Ui.Text(UsageFormat.Tokens(tokens), 10.5, Ui.Medium, 0.55), percent);
        var bar = new UsageBar(3, showsGlow: false);
        bar.Loaded += (_, _) => bar.Update(share, tint);
        return Ui.Column(4, Ui.Spread(Ui.Text(name, 11, Ui.Medium, 0.8), figures), bar);
    }
}
