using Microsoft.UI.Xaml;

namespace Claudio.App;

/// <summary>Claudio starts as the floating card, with its icon in the notification area.</summary>
public partial class App : Application
{
    private CardWindow? _card;
    private UsageRefresher? _refresher;
    private TrayController? _tray;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var card = new CardWindow();
        _card = card;
        _tray = new TrayController(card.DispatcherQueue, card.Toggle, () => _refresher!.RefreshAsync(userInitiated: true), Quit);
        _refresher = new UsageRefresher(card.DispatcherQueue, (summary, totals) =>
        {
            card.Show(summary, totals);
            _tray.Show(summary, card.TintOf(summary));
        });
        card.Activate();
        _ = _refresher.RefreshAsync();
        _ = Updates.CheckAsync();
    }

    /// <summary>The icon goes first, or Windows leaves a ghost of it next to the clock.</summary>
    private void Quit()
    {
        _tray?.Dispose();
        _refresher?.Dispose();
        _card?.Close();
        Exit();
    }
}
