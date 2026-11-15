using Claudio.Core.Design;
using Claudio.Core.Models;
using Claudio.Core.Services;
using Claudio.Core.ViewModels;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Claudio.App;

/// <summary>
/// Claudio starts where it was left: the floating card, the notification area, or the island at the
/// top of the screen, with its icon next to the clock in every case. One view model drives every
/// face, as Claudy's app delegate wires its card, menu bar item and island to one; the sessions'
/// view model feeds them all the agents and the waiting sessions, and the alerts.
/// </summary>
public partial class App : Application
{
    private UsageViewModel? _model;
    private PortsViewModel? _ports;
    private AgentsViewModel? _agents;
    private WindowsSessions? _sessions;
    private Notifier? _notifier;
    private GlobalHotKey? _hotKey;
    private GlobalHotKey? _roomHotKey;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _agentsTimer;
    private readonly QuotaAlerts _quotaAlerts = new();
    private readonly ActivityAlerts _activityAlerts = new();
    private UsageSnapshot? _alerted;

    /// <summary>The open sessions' ids, read by the ports scan on its own thread.</summary>
    private volatile IReadOnlySet<string> _liveSessions = new HashSet<string>();
    private UpdateChecker? _updates;
    private RootView? _card;
    private MenuBarPopover? _flyout;
    private MenuBarController? _tray;
    private UpdateBubblePanel? _bubble;
    private NotchController? _notch;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _screens;
    private Placement? _placed;
    private bool _portsOnScreen;

    public App()
    {
        InitializeComponent();
        // A failure that escapes a view is written down rather than lost: it is what a bug report needs.
        // A widget that vanishes is worse than one view that failed: the failure is written down and
        // Claudio carries on, whichever thread it came from.
        UnhandledException += (_, args) =>
        {
            DiagnosticLog.Append($"unhandled: {args.Exception}");
            args.Handled = true;
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            DiagnosticLog.Append($"unobserved: {args.Exception}");
            args.SetObserved();
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => DiagnosticLog.Append($"fatal: {args.ExceptionObject}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Start();
        }
#pragma warning disable CA1031 // A start that failed must leave a trace, and must not leave a process with nothing on screen.
        catch (Exception error)
#pragma warning restore CA1031
        {
            DiagnosticLog.Append($"start failed: {error}");
            Exit();
        }
    }

    private void Start()
    {
#if DEBUG
        // `--room-preview`: the agent room alone, on a made-up scene, and nothing else of Claudio.
        if (RoomPreviewWindow.IsRequested)
        {
            var preview = new RoomPreviewWindow();
            preview.Closed += (_, _) => Exit();
            preview.Activate();
            return;
        }
#endif
        var ui = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var model = new UsageViewModel(ui);
        var updates = new UpdateChecker(ui);
        // A server of a session still open is never listed: the ports scan asks which are.
        var ports = new PortsViewModel(ui, () => _liveSessions);
        var sessions = new WindowsSessions();
        var agents = new AgentsViewModel(AgentScanner.Create(model.Home, sessions.Identity), sessions);
        var notifier = new Notifier(ui);
        _model = model;
        _updates = updates;
        _ports = ports;
        _sessions = sessions;
        _agents = agents;
        _notifier = notifier;
        _hotKey = new GlobalHotKey('C', 1, GlobalHotKey.Display, ToggleClaudy);
        _roomHotKey = new GlobalHotKey('R', 2, GlobalHotKey.RoomDisplay, ToggleRoom);
        var actions = new ClaudioActions(ToggleClaudy, ToggleRoom, ExportUsage, Quit,
                                         () => _hotKey?.IsRegistered == true, () => _roomHotKey?.IsRegistered == true);
        ClaudyMenu Menu() => new(model, notifier, actions);
        void ShowWidget() => model.Place(Placement.Widget);

        var card = new RootView(model, updates, ports, agents, Menu(), ToggleRoom, Quit);
        var flyout = new MenuBarPopover(model, updates, agents, ShowWidget);
        _card = card;
        _flyout = flyout;
        _tray = new MenuBarController(ui, TrayClicked, model, Menu());
        _bubble = new UpdateBubblePanel(updates, CloseBubble);
        // Every window is made at launch: the room's is shown only when it is opened.
        _roomWindow = new RoomWindow();
        _roomWindow.CloseRequested += CloseRoom;
        // The bubble follows the card when it is dragged or changes size.
        card.Placed += Announce;
        _notch = new NotchController(model, updates, agents, ui, Menu(), ShowWidget);
        // The open island carries the update line: the bubble has been answered.
        _notch.Opened += () =>
        {
            if (_updates?.ShouldAnnounce == true)
            {
                CloseBubble();
            }
        };
        model.HasNotchedScreen = CurrentNotch().Geometry is not null;
        // Screens came or went, or the taskbar moved: the island leaves a notch that moved at
        // once, then everything finds its place once the system has settled.
        var screens = ui.CreateTimer();
        screens.Interval = TimeSpan.FromMilliseconds(500);
        screens.IsRepeating = false;
        screens.Tick += (_, _) =>
        {
            screens.Stop();
            ScreensChanged();
        };
        _screens = screens;
        TransparentBackdrop.DisplayChanged += () => ui.TryEnqueue(() =>
        {
            _notch?.HideIfMoved(CurrentNotch().Geometry);
            screens.Stop();
            screens.Start();
        });

        model.OnUserRefresh = updates.CheckNow;
        model.Changed += Render;
        model.Changed += AlertQuota;
        updates.Changed += Render;
        ports.Changed += Render;
        agents.Changed += Render;
        notifier.Changed += Render;
        BindAlerts();

        card.Present();
        Place();
        Render();
        ports.Start();
        StartAgents(ui);
        notifier.Register();
        _hotKey.Register();
        _roomHotKey.Register();
        updates.Start();
        _ = Logged(model.RefreshAsync());
        // `--room` on the command line opens the agent room at launch.
        if (Environment.GetCommandLineArgs().Contains(RoomArgument))
        {
            ToggleRoom();
        }
    }

    /// <summary>Claudy's <c>--room</c>: the agent room opens at launch.</summary>
    private const string RoomArgument = "--room";

    /// <summary>
    /// The sessions are read every few seconds, faster while something works: the view model says
    /// how long to wait after each pass.
    /// </summary>
    private void StartAgents(Microsoft.UI.Dispatching.DispatcherQueue ui)
    {
        var timer = ui.CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = TimeSpan.FromMilliseconds(10);
        timer.Tick += async (_, _) =>
        {
            if (_agents is not { } agents)
            {
                return;
            }
            try
            {
                await agents.RefreshAsync();
            }
#pragma warning disable CA1031 // A pass that fails is written down; the next one tries again.
            catch (Exception error)
#pragma warning restore CA1031
            {
                DiagnosticLog.Append($"sessions: {error.Message}");
            }
            _liveSessions = agents.Sessions.Select(session => session.Id).ToHashSet();
            timer.Interval = agents.Interval ?? AgentsViewModel.QuietInterval;
            timer.Start();
        };
        _agentsTimer = timer;
        timer.Start();
    }

    /// <summary>
    /// Every pass over the sessions goes through the rules, as every reading of the quota does;
    /// what they call for is posted when its kind is switched on. A click on a banner about a
    /// session brings that session's app forward; one about an agent opens the room on its desk.
    /// </summary>
    private void BindAlerts()
    {
        if (_agents is not { } agents || _notifier is not { } notifier)
        {
            return;
        }
        agents.OnPass = activity =>
        {
            var result = _activityAlerts.Evaluate(activity.Sessions, activity.Agents, DateTimeOffset.UtcNow);
            notifier.Withdraw(result.Withdraw);
            notifier.Deliver(result.Post);
        };
        notifier.OnOpenSession = agents.Focus;
        notifier.OnOpenAgent = ShowAgent;
    }

    /// <summary>Each new reading of the quota, the demo set aside: nothing in it deserves a banner.</summary>
    private void AlertQuota()
    {
        if (_model is not { HasLoaded: true } model || _notifier is null || ReferenceEquals(model.Snapshot, _alerted))
        {
            return;
        }
        _alerted = model.Snapshot;
        if (!model.Snapshot.IsDemo)
        {
            _notifier.Deliver(_quotaAlerts.Evaluate(model.Snapshot.Session, DateTimeOffset.UtcNow));
        }
    }

    /// <summary>The card's window, for the export's dialogs.</summary>
    private void ExportUsage()
    {
        if (_model is { } model && _card is { } card)
        {
            model.ExportUsage(WinRT.Interop.WindowNative.GetWindowHandle(card));
        }
    }

    /// <summary>
    /// Ctrl+Alt+C, Claudy's ⌃⌥C: Claudio where it stands, shown or put away. The card comes and
    /// goes, the short card above the clock opens and closes, the island opens and closes.
    /// </summary>
    private void ToggleClaudy()
    {
        switch (_model?.Placement.Effective(_model.HasNotchedScreen))
        {
            case Placement.Widget:
                _card?.Toggle();
                _updates?.AcknowledgeGreeting();
                break;
            case Placement.Notch:
                if (_notch?.ToggleFromShortcut() != true)
                {
                    _flyout?.Toggle();
                }
                break;
            default:
                _flyout?.Toggle();
                break;
        }
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
        if (_model is null || _card is null || _flyout is null || _tray is null || _updates is null || _notch is null)
        {
            return;
        }
        if (_placed != _model.Placement.Effective(_model.HasNotchedScreen))
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
        _notch.Update();
        if (_agents is { } agents)
        {
            // Finished agents stay listed a while; the icons count the ones still at it, in the
            // colour of the one at work.
            var lead = agents.Lead;
            _tray.ShowTeam(agents.RunningCount, lead is null ? null : agents.Tint(lead), agents.Waiting.Count);
        }
        _tray.Show(_model.Snapshot, _updates.IsGreeting, _updates.Available is not null, _model.HasLoaded);
        Announce();

        // The ports tab scans every few seconds while it is on screen, slowly otherwise: all a
        // hidden tab owes the user is a correct badge.
        var portsOnScreen = _model.Tab == Views.CardTab.Ports && _model.Placement == Placement.Widget && !_model.IsMinimal;
        RenderRoom();
        if (portsOnScreen != _portsOnScreen && _ports is not null)
        {
            _portsOnScreen = portsOnScreen;
            _ports.SetVisible(portsOnScreen);
        }
    }

    /// <summary>
    /// A new version pops up once, as Claudy's bubble drops from its menu bar item: above the clock
    /// when Claudio lives in the notification area, against the card when it floats, so it is seen
    /// whatever the placement. Answered, it does not come back for that version.
    /// </summary>
    private void Announce()
    {
        if (_bubble is null || _updates is null || _model is null || _card is null || _notch is null)
        {
            return;
        }
        if (!_updates.ShouldAnnounce || _updates.Available is null)
        {
            _bubble.Dismiss();
            return;
        }
        switch (_model.Placement.Effective(_model.HasNotchedScreen))
        {
            case Placement.Widget:
                _bubble.Present(_card.VisualBounds);
                break;
            case Placement.Notch when _notch.RestingBounds is { } island:
                // Just under the ears, centred on them.
                _bubble.Present(island, centred: true);
                break;
            case Placement.Notch:
                // The island stepped aside for a full-screen app: so does the bubble, for now.
                _bubble.Dismiss();
                break;
            default:
                _bubble.Present();
                break;
        }
    }

    /// <summary>The bubble was seen and closed: it does not come back for this version.</summary>
    private void CloseBubble()
    {
        _bubble?.Dismiss();
        _updates?.MarkAnnounced();
    }

    /// <summary>Exactly one of the card, the notification-area flyout and the island.</summary>
    private void Place()
    {
        if (_model is null || _card is null || _flyout is null || _notch is null)
        {
            return;
        }
        var placement = _model.Placement.Effective(_model.HasNotchedScreen);
        _placed = placement;
        if (placement != Placement.Widget)
        {
            _card.HideCard();
        }
        if (placement != Placement.NotificationArea)
        {
            _flyout.HideFlyout();
        }
        if (placement != Placement.Notch)
        {
            _notch.Hide();
        }
        if (placement == Placement.Widget)
        {
            _card.ShowCard();
        }
        // Effective says the island only with a notch, but the screens may have changed since.
        else if (placement == Placement.Notch && CurrentNotch() is { Geometry: { } geometry } notch)
        {
            _notch.Show(geometry, notch.Scale);
        }
    }

    /// <summary>
    /// Screens came or went, or changed resolution: the island follows the notch, or hands over to
    /// the notification area when no screen can hold it.
    /// </summary>
    private void ScreensChanged()
    {
        if (_model is null || _notch is null)
        {
            return;
        }
        var (geometry, scale) = CurrentNotch();
        _model.HasNotchedScreen = geometry is not null;
        // Same placement, new resolution: the notch moved with it.
        if (geometry is not null && _placed == Placement.Notch)
        {
            _notch.Show(geometry, scale);
        }
        Render();
    }

    /// <summary>
    /// Where the island goes: no PC has a notch, so Claudio gives the main screen one, at the top of
    /// its work area, unless <c>--simulate-notch</c> (none, or WIDTHxHEIGHT) says otherwise. In
    /// units, with the main screen's scale to turn them into pixels.
    /// </summary>
    private static (NotchGeometry? Geometry, double Scale) CurrentNotch()
    {
        var primary = DisplayArea.Primary;
        var displays = new List<DisplayArea> { primary };
        var all = DisplayArea.FindAll();
        for (var index = 0; index < all.Count; index++)
        {
            if (all[index].DisplayId.Value != primary.DisplayId.Value)
            {
                displays.Add(all[index]);
            }
        }
        var screens = new List<ScreenMetrics>();
        var scales = new List<double>();
        foreach (var display in displays)
        {
            var bounds = display.OuterBounds;
            var scale = PanelChrome.ScaleAt(new Windows.Graphics.PointInt32(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2)));
            var work = display.WorkArea;
            scales.Add(scale);
            screens.Add(new ScreenMetrics(new ScreenRect(work.X / scale, work.Y / scale, work.Width / scale, work.Height / scale)));
        }
        return (NotchGeometry.Current(screens, NotchSimulation), scales[0]);
    }

    /// <summary>Claudy's <c>-ClaudySimulateNotch</c>: <c>--simulate-notch none</c>, or <c>--simulate-notch 185x32</c>.</summary>
    private static string? NotchSimulation
    {
        get
        {
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, "--simulate-notch");
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }
    }

    /// <summary>
    /// A click on the icon: the card comes and goes, the flyout opens above the clock, or the island
    /// opens at the top of the screen.
    /// </summary>
    private void TrayClicked()
    {
        var placement = _model?.Placement.Effective(_model.HasNotchedScreen);
        if (placement == Placement.Notch)
        {
            _notch?.Toggle();
        }
        else if (placement == Placement.NotificationArea)
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
        _agentsTimer?.Stop();
        _hotKey?.Dispose();
        _roomHotKey?.Dispose();
        _notifier?.Dispose();
        _agents?.Dispose();
        CloseRoom();
        _tray?.Dispose();
        _notch?.Dispose();
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
