using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Claudio.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Claudio.App;

/// <summary>
/// Sends Claudio's notifications, each kind behind its own switch, and hands the session or agent a
/// banner spoke of to the app when it is clicked, as Claudy's <c>Notifier</c>.
///
/// Windows notifications come from the Windows App SDK's <see cref="AppNotificationManager"/>,
/// which an unpackaged app registers for itself: <see cref="Register"/> at launch, before anything
/// is posted, <see cref="Dispose"/> at exit, and <see cref="UnregisterAll"/> at uninstall, which
/// <c>Program</c> hooks into Velopack. Windows asks the user nothing, unlike macOS: notifications
/// are on until the user turns them off in Settings ▸ System ▸ Notifications, which
/// <see cref="IsDenied"/> reports and <see cref="OpenSettings"/> leads to.
///
/// Every call is made on the UI thread, and none throws: a system that will not notify is logged,
/// and Claudio carries on silent.
/// </summary>
internal sealed class Notifier : IDisposable
{
    /// <summary>Claudy's thread identifiers, as Windows' notification groups.</summary>
    private const string QuotaGroup = "quota";
    private const string SessionsGroup = "sessions";

    private const string SessionArgument = "sessionID";
    private const string AgentArgument = "agentID";

    /// <summary>Windows takes a tag of 64 characters at most.</summary>
    private const int MaximumTag = 64;

    private readonly DispatcherQueue _ui;
    private readonly HashSet<NotificationKind> _enabled;
    private bool _isRegistered;
    private bool _hasTriedToRegister;

    public Notifier(DispatcherQueue ui)
    {
        _ui = ui;
        _enabled = NotificationKinds.All.Where(Preferences.IsNotifying).ToHashSet();
    }

    /// <summary>Raised on the UI thread when a switch or <see cref="IsDenied"/> changes, for the menu to follow.</summary>
    public event Action? Changed;

    /// <summary>A click on a banner about a session, with its id. Called on the UI thread.</summary>
    public Action<string>? OnOpenSession { get; set; }

    /// <summary>
    /// A click on a banner about an agent, with its id. Without it, such a click opens the agent's
    /// session. Called on the UI thread.
    /// </summary>
    public Action<string>? OnOpenAgent { get; set; }

    /// <summary>
    /// The user turned Claudio's notifications off in Settings, or a policy did: the switches can
    /// do nothing until they are turned back on there, and the menu says so.
    /// </summary>
    public bool IsDenied { get; private set; }

    /// <summary>
    /// Registers Claudio with Windows' notifications, which an unpackaged app must do before it
    /// posts anything, and reads whether they are allowed. Set <see cref="OnOpenSession"/> and
    /// <see cref="OnOpenAgent"/> first: when Claudio was launched by a click on one of its
    /// banners, that click is handed over here, once the UI thread is free.
    /// </summary>
    public void Register()
    {
        if (_isRegistered || _hasTriedToRegister)
        {
            return;
        }
        // One attempt: a system that refused once is not asked again at every alert.
        _hasTriedToRegister = true;
        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                DiagnosticLog.Append("notifications: not supported on this system");
                return;
            }
            var manager = AppNotificationManager.Default;
            // Subscribed before registering, or a click that launched Claudio would be lost.
            manager.NotificationInvoked += OnInvoked;
            try
            {
                manager.Register();
            }
            catch (Exception error) when (IsSystemFailure(error))
            {
                manager.NotificationInvoked -= OnInvoked;
                throw;
            }
            _isRegistered = true;

            if (AppInstance.GetCurrent().GetActivatedEventArgs() is { Kind: ExtendedActivationKind.AppNotification, Data: AppNotificationActivatedEventArgs launch })
            {
                Dispatch(launch);
            }
        }
        catch (Exception error) when (IsSystemFailure(error))
        {
            DiagnosticLog.Append($"notifications: registration failed: {error.Message}");
        }
        RefreshPermission();
    }

    /// <summary>
    /// Removes everything Windows keeps about Claudio's notifications: for the uninstaller, which
    /// runs before any window. Never throws, whatever goes wrong: an uninstall must not stop on a
    /// notification left registered.
    /// </summary>
    public static void UnregisterAll()
    {
        try
        {
            if (AppNotificationManager.IsSupported())
            {
                AppNotificationManager.Default.UnregisterAll();
            }
        }
#pragma warning disable CA1031 // The uninstaller goes on whatever this throws.
        catch (Exception error)
#pragma warning restore CA1031
        {
            DiagnosticLog.Append($"notifications: unregistration failed: {error.Message}");
        }
    }

    /// <summary>
    /// Reads whether notifications are allowed, without asking: at launch, and as a menu opens,
    /// since the user may have changed it in Settings meanwhile.
    /// </summary>
    public void RefreshPermission()
    {
        var isDenied = Setting() is AppNotificationSetting.DisabledForApplication
                                  or AppNotificationSetting.DisabledForUser
                                  or AppNotificationSetting.DisabledByGroupPolicy;
        if (isDenied != IsDenied)
        {
            IsDenied = isDenied;
            Changed?.Invoke();
        }
    }

    public bool IsEnabled(NotificationKind kind) => _enabled.Contains(kind);

    public void Toggle(NotificationKind kind) => Set(kind, !IsEnabled(kind));

    /// <summary>A switch, kept across launches.</summary>
    public void Set(NotificationKind kind, bool isOn)
    {
        var changed = isOn ? _enabled.Add(kind) : _enabled.Remove(kind);
        Preferences.SetNotifying(kind, isOn);
        if (changed)
        {
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Posts the alerts whose kind is switched on. Windows asks no permission: they go out unless
    /// the user turned Claudio's notifications off, which this reading of the setting learns.
    /// </summary>
    public void Deliver(IEnumerable<Alert> alerts)
    {
        ArgumentNullException.ThrowIfNull(alerts);
        var wanted = alerts.Where(alert => IsEnabled(alert.Kind)).ToList();
        if (wanted.Count == 0)
        {
            return;
        }
        Register();
        RefreshPermission();
        if (!_isRegistered || Setting() != AppNotificationSetting.Enabled)
        {
            return;
        }
        foreach (var alert in wanted)
        {
            Post(alert);
        }
    }

    /// <summary>Takes back banners whose event no longer holds: a session answered elsewhere.</summary>
    public void Withdraw(IEnumerable<string> ids)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (!_isRegistered)
        {
            return;
        }
        foreach (var id in ids)
        {
            _ = RemoveAsync(Tag(id));
        }
    }

    /// <summary>Settings ▸ System ▸ Notifications, where Claudio's switch is.</summary>
    public static void OpenSettings() => Shell.Open(new Uri("ms-settings:notifications"));

    /// <summary>
    /// Unregisters at exit, so a click on a banner left in the notification centre launches
    /// Claudio again rather than calling into a process that is gone.
    /// </summary>
    public void Dispose()
    {
        if (!_isRegistered)
        {
            return;
        }
        _isRegistered = false;
        try
        {
            var manager = AppNotificationManager.Default;
            manager.NotificationInvoked -= OnInvoked;
            manager.Unregister();
        }
        catch (Exception error) when (IsSystemFailure(error))
        {
            DiagnosticLog.Append($"notifications: unregistration failed: {error.Message}");
        }
    }

    /// <summary>
    /// The tag is the alert's id, so posting the same event again replaces its banner and
    /// <see cref="Withdraw"/> finds it; the group is Claudy's thread.
    /// </summary>
    private static void Post(Alert alert)
    {
        try
        {
            var builder = new AppNotificationBuilder()
                .AddText(alert.Title)
                .AddText(alert.Body)
                .SetTag(Tag(alert.Id))
                .SetGroup(alert.Kind.IsQuota() ? QuotaGroup : SessionsGroup);
            if (alert.SessionId is { } session)
            {
                builder.AddArgument(SessionArgument, session);
            }
            if (alert.AgentId is { } agent)
            {
                builder.AddArgument(AgentArgument, agent);
            }
            AppNotificationManager.Default.Show(builder.BuildNotification());
        }
        catch (Exception error) when (IsSystemFailure(error))
        {
            DiagnosticLog.Append($"notifications: post failed: {error.Message}");
        }
    }

    private static async Task RemoveAsync(string tag)
    {
        try
        {
            await AppNotificationManager.Default.RemoveByTagAsync(tag);
        }
        catch (Exception error) when (IsSystemFailure(error))
        {
            DiagnosticLog.Append($"notifications: withdrawal failed: {error.Message}");
        }
    }

    /// <summary>An id longer than Windows takes is hashed, the same way each time.</summary>
    private static string Tag(string id) =>
        id.Length <= MaximumTag ? id : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..32];

    private static AppNotificationSetting Setting()
    {
        try
        {
            return AppNotificationManager.IsSupported() ? AppNotificationManager.Default.Setting : AppNotificationSetting.Unsupported;
        }
        catch (Exception error) when (IsSystemFailure(error))
        {
            DiagnosticLog.Append($"notifications: setting unreadable: {error.Message}");
            return AppNotificationSetting.Unsupported;
        }
    }

    /// <summary>Windows raises a click on a worker thread.</summary>
    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args) => Dispatch(args);

    /// <summary>The arguments are copied before the hop: the event's object belongs to the thread that raised it.</summary>
    private void Dispatch(AppNotificationActivatedEventArgs args)
    {
        var arguments = new Dictionary<string, string>(args.Arguments, StringComparer.Ordinal);
        _ui.TryEnqueue(() => Open(arguments));
    }

    /// <summary>A banner about an agent leads to its desk when the app can show it; any other to the session.</summary>
    private void Open(Dictionary<string, string> arguments)
    {
        if (!arguments.TryGetValue(SessionArgument, out var session))
        {
            return;
        }
        if (arguments.TryGetValue(AgentArgument, out var agent) && OnOpenAgent is { } openAgent)
        {
            openAgent(agent);
        }
        else
        {
            OnOpenSession?.Invoke(session);
        }
    }

    /// <summary>
    /// What a system that will not notify throws: a COM error, a missing runtime component, a
    /// refused registration. Anything else is a bug and is left to surface.
    /// </summary>
    private static bool IsSystemFailure(Exception error) =>
        error is COMException or InvalidOperationException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or NotImplementedException or TypeLoadException or TypeInitializationException
            or DllNotFoundException or EntryPointNotFoundException or FileNotFoundException;
}
