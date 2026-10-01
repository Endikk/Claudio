using Claudio.Core.Design;
using Claudio.Core.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Claudio.App.Views;

/// <summary>
/// The bubble that rises from the notification area when a new version is out, once per version,
/// as Claudy's drops from its menu bar item: the mascot waving, the version, what's new, and the
/// choice between later and now.
/// </summary>
internal sealed partial class UpdateBubble : StackPanel
{
    private readonly UpdateChecker _updates;
    private readonly MascotView _mascot = new() { Width = 38, Height = 42, IsWaving = true };
    private readonly TextBlock _title = Ui.Text(string.Empty, 13, Ui.SemiBold, 0.92);
    private readonly TextBlock _current = Ui.Text(string.Empty, 10.5, Ui.Medium, 0.5);
    private readonly TextBlock _later = Ui.Text("Later", 11.5, Ui.SemiBold, 0.75);
    private readonly ContentControl _action = new();

    public UpdateBubble(UpdateChecker updates, Action close)
    {
        _updates = updates;
        Spacing = 12;
        Padding = new Thickness(14);
        Width = 256;

        var notes = Ui.Text("What's new", 10.5, Ui.Medium, 0.7);
        notes.TextDecorations = Windows.UI.Text.TextDecorations.Underline;
        var line = Ui.Row(4, _current, Ui.Plain(notes, updates.OpenReleaseNotes));
        var heading = Ui.Column(3, _title, line);
        heading.VerticalAlignment = VerticalAlignment.Center;
        Children.Add(Ui.Row(10, _mascot, heading));
        Children.Add(Ui.Spread(Ui.Plain(_later, close), _action, minimumGap: 8));
    }

    public void Update()
    {
        Ui.Restyle(_title, $"Claudio {_updates.Available} is out", 0.92);
        Ui.Restyle(_current, $"You have {UpdateChecker.Current} ·", 0.5);
        _mascot.Tint = Theme.Color(Accent.Coral);
        var busy = _updates.State is UpdateState.Downloading or UpdateState.Restarting;
        _later.Foreground = Ui.Primary(busy ? 0.35 : 0.75);
        var coral = Theme.Color(Accent.Coral);
        _action.Content = _updates.State switch
        {
            UpdateState.Ready => Ui.Plain(Ui.Text("Update", 11.5, Ui.SemiBold, 1, coral), _updates.Update,
                                          "Claudio restarts in the new version"),
            UpdateState.Downloading => Ui.Text("Downloading…", 11.5, Ui.Medium, 0.6),
            UpdateState.Restarting => Ui.Text("Restarting…", 11.5, Ui.Medium, 0.6),
            _ => Ui.Plain(Ui.Text("Download", 11.5, Ui.SemiBold, 1, coral), UpdateChecker.OpenReleasePage,
                          "Open the release page to download it"),
        };
    }
}
