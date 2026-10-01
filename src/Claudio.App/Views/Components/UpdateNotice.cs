using Claudio.Core.Design;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>One slim line above the footer, only while a newer Claudio is out.</summary>
internal sealed partial class UpdateRow : Grid
{
    private readonly UpdateChecker _updates;
    private readonly TextBlock _text = Ui.Text(string.Empty, 11, Ui.Medium, 0.8);
    private readonly ContentControl _action = new();

    public UpdateRow(UpdateChecker updates)
    {
        _updates = updates;
        Padding = new Thickness(10, 7, 10, 7);
        CornerRadius = new CornerRadius(9);
        Children.Add(Ui.Spread(Ui.Row(7, Ui.Dot(6, Theme.Color(Accent.Coral)), _text), _action));
    }

    public void Update()
    {
        if (_updates.Available is not { } version)
        {
            Visibility = Visibility.Collapsed;
            return;
        }
        Visibility = Visibility.Visible;
        Background = Ui.Brush(Theme.Color(Accent.Coral), 0.12);
        Ui.Restyle(_text, _updates.State == UpdateState.Failed ? "Update failed" : $"Claudio {version} is available", 0.8);
        var coral = Theme.Color(Accent.Coral);
        _action.Content = _updates.State switch
        {
            UpdateState.Ready => Ui.Plain(Ui.Text("Update", 11, Ui.SemiBold, 1, coral), _updates.Update,
                                          "Claudio restarts in the new version"),
            UpdateState.Downloading => Ui.Text("Downloading…", 11, Ui.Medium, 0.6),
            UpdateState.Restarting => Ui.Text("Restarting…", 11, Ui.Medium, 0.6),
            _ => Ui.Plain(Ui.Text("Download", 11, Ui.SemiBold, 1, coral), UpdateChecker.OpenReleasePage,
                          "Open the release page to download it"),
        };
    }
}
