using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Velopack;

namespace Claudio.App;

/// <summary>
/// The entry point. Velopack runs first: when the installer or an update launches Claudio to
/// create its shortcuts or finish an update, it handles that and exits before any window.
/// </summary>
public static class Program
{
    /// <summary>One Claudio per session, however many times its shortcut is opened.</summary>
    private const string InstanceName = @"Local\Claudio.SingleInstance";

    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().Run();

        using var instance = new Mutex(initiallyOwned: true, InstanceName, out var isFirst);
        if (!isFirst)
        {
            return;
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(callback =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
