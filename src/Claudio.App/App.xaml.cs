using Microsoft.UI.Xaml;

namespace Claudio.App;

/// <summary>Claudio starts as the floating card, Claudy's first face.</summary>
public partial class App : Application
{
    private CardWindow? _card;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _card = new CardWindow();
        _card.Activate();
        _ = Updates.CheckAsync();
    }
}
