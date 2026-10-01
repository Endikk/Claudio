using Claudio.Core.Design;
using Claudio.Core.Models;
using Microsoft.UI.Xaml;

namespace Claudio.App;

/// <summary>
/// Claudio starts as the floating card, or in the notification area when that is where it was
/// left, with its icon next to the clock either way. One view model drives every face, as
/// Claudy's app delegate wires its card, menu bar item and island to one.
/// </summary>
public partial class App : Application
{
    private UsageViewModel? _model;
    private PortsViewModel? _ports;
    private UpdateChecker? _updates;
    private RootView? _card;
    private MenuBarPopover? _flyout;
    private MenuBarController? _tray;
    private UpdateBubblePanel? _bubble;
    private Placement? _placed;
    private bool _portsOnScreen;

    public App()
    {
        InitializeComponent();
        // A failure that escapes a view is written down rather than lost: it is what a bug report needs.
        UnhandledException += (_, args) => DiagnosticLog.Append($"unhandled: {args.Exception}");
        TaskScheduler.UnobservedTaskException += (_, args) => DiagnosticLog.Append($"unobserved: {args.Exception}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var model = new UsageViewModel(ui);
        var updates = new UpdateChecker(ui);
        var ports = new PortsViewModel(ui);
        _model = model;
        _updates = updates;
        _ports = ports;

        var card = new RootView(model, updates, ports, Quit);
        var flyout = new MenuBarPopover(model, updates, () => model.Place(Placement.Widget));
        _card = card;
        _flyout = flyout;
        _tray = new MenuBarController(ui, TrayClicked, model, Quit);
        _bubble = new UpdateBubblePanel(updates, CloseBubble);

        model.OnUserRefresh = updates.CheckNow;
        model.Changed += Render;
        updates.Changed += Render;
        ports.Changed += Render;

        card.Present();
        Place();
        Render();
        ports.Start();
        updates.Start();
        _ = Logged(model.RefreshAsync());
    }

    private static async Task Logged(Task task)
    {
        try
        {
            await task;
        }
#pragma warning disable CA1031 // Logged, never thrown at the user.
        catch (Exception error)
#pragma warning restore CA1031
        {
            DiagnosticLog.Append($"refresh: {error}");
        }
    }

    /// <summary>Redraws every face from the model. A view that fails is written down, never fatal.</summary>
    private void Render()
    {
        try
        {
            RenderFaces();
        }
#pragma warning disable CA1031 // A broken view must not take Claudio, and its tray icon, down.
        catch (Exception error)
#pragma warning restore CA1031
        {
            DiagnosticLog.Append($"render: {error}");
        }
    }

    private void RenderFaces()
    {
        if (_model is null || _card is null || _flyout is null || _tray is null || _updates is null)
        {
            return;
        }
        if (_placed != _model.Placement)
        {
            // Moving Claudio elsewhere waves again while an update is pending.
            if (_placed is not null)
            {
                _updates.GreetAgain();
            }
            Place();
        }
        _card.Update();
        _flyout.Update();
        _tray.Show(_model.Snapshot, _updates.IsGreeting, _updates.Available is not null, _model.HasLoaded);
        Announce();

        // The ports tab scans every few seconds while it is on screen, slowly otherwise: all a
        // hidden tab owes the user is a correct badge.
        var portsOnScreen = _model.Tab == Views.CardTab.Ports && _model.Placement == Placement.Widget && !_model.IsMinimal;
        if (portsOnScreen != _portsOnScreen && _ports is not null)
        {
            _portsOnScreen = portsOnScreen;
            _ports.SetVisible(portsOnScreen);
        }
    }

    /// <summary>
    /// The bubble rises from the notification area once per version, as Claudy's drops from its
    /// menu bar item; on the floating card the update line and the waving mascot say it instead.
    /// </summary>
    private void Announce()
    {
        if (_bubble is null || _updates is null || _model is null)
        {
            return;
        }
        if (_updates.ShouldAnnounce && _updates.Available is not null && _model.Placement == Placement.NotificationArea)
        {
            _bubble.Present();
        }
        else
        {
            _bubble.Dismiss();
        }
    }

    /// <summary>The bubble was seen and closed: it does not come back for this version.</summary>
    private void CloseBubble()
    {
        _bubble?.Dismiss();
        _updates?.MarkAnnounced();
    }

    /// <summary>Exactly one of the card and the notification-area flyout.</summary>
    private void Place()
    {
        if (_model is null || _card is null || _flyout is null)
        {
            return;
        }
        _placed = _model.Placement;
        if (_model.Placement == Placement.Widget)
        {
            _flyout.HideFlyout();
            _card.ShowCard();
        }
        else
        {
            _card.HideCard();
        }
    }

    /// <summary>A click on the icon: the card comes and goes, or the flyout opens above the clock.</summary>
    private void TrayClicked()
    {
        if (_model?.Placement == Placement.NotificationArea)
        {
            if (_updates?.ShouldAnnounce == true)
            {
                CloseBubble();
            }
            _flyout?.Toggle();
        }
        else
        {
            _card?.Toggle();
        }
        _updates?.AcknowledgeGreeting();
    }

    /// <summary>The icon goes first, or Windows leaves a ghost of it next to the clock.</summary>
    private void Quit()
    {
        _tray?.Dispose();
        _updates?.ApplyOnExit();
        _updates?.Dispose();
        _ports?.Dispose();
        _model?.Dispose();
        _bubble?.Close();
        _flyout?.Close();
        _card?.Close();
        Exit();
    }
}
