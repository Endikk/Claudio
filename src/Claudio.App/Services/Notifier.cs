using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Claudio.Core.Services;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Claudio.App;

/// <summary>
/// Sends Claudio's notifications, each kind behind its own switch, and hands the session or agent a
/// banner spoke of to the app when it is clicked, as Claudy's <c>Notifier</c>.
///
/// Windows' own toasts carry them. The Windows App SDK's app notifications need its Singleton
/// package, which a self-contained Claudio does not have (registering failed with "module not
/// found"), so Claudio files itself under an app id of its own in the user's registry, as Windows
/// lets an unpackaged app do: <see cref="Register"/> at launch, before anything is posted, and
/// <see cref="UnregisterAll"/> at uninstall, which <c>Program</c> hooks into Velopack. A click on a
/// banner reaches Claudio while it runs. Windows asks the user nothing, unlike macOS:
/// notifications are on until the user turns them off in Settings ▸ System ▸ Notifications, which
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

    /// <summary>The app id Windows files Claudio's banners under.</summary>
    private const string AppId = "Endikk.Claudio";
    private const string AppIdKey = @"Software\Classes\AppUserModelId\" + AppId;

    private const string SessionArgument = "sessionID";
    private const string AgentArgument = "agentID";

    /// <summary>Windows takes a tag of 64 characters at most.</summary>
    private const int MaximumTag = 64;

    private readonly DispatcherQueue _ui;
    private readonly HashSet<NotificationKind> _enabled;
    /// <summary>The banners on show: Windows raises their click on the object itself, which must live.</summary>
    private readonly Dictionary<string, ToastNotification> _shown = [];
    private ToastNotifier? _notifier;
    private bool _isRegistered;
    private bool _hasLoggedSetting;
    private bool _hasTriedToRegister;

    public Notifier(DispatcherQueue ui)
    {
        _ui = ui;
        _enabled = Claudio.Core.Services.NotificationKinds.All.Where(Preferences.IsNotifying).ToHashSet();
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
            using (var key = Registry.CurrentUser.CreateSubKey(AppIdKey))
            {
                key.SetValue("DisplayName", "Claudio");
                key.SetValue("IconUri", Path.Combine(AppContext.BaseDirectory, "Assets", "Claudio.png"));
                key.SetValue("ShowInSettings", 1, RegistryValueKind.DWord);
            }
            _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
            _isRegistered = true;
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
            ToastNotificationManager.History.Clear(AppId);
        }
#pragma warning disable CA1031 // The uninstaller goes on whatever this throws.
        catch (Exception error)
#pragma warning restore CA1031
        {
            DiagnosticLog.Append($"notifications: history not cleared: {error.Message}");
        }
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(AppIdKey, throwOnMissingSubKey: false);
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
        var isDenied = Setting() is NotificationSetting.DisabledForApplication
                                  or NotificationSetting.DisabledForUser
                                  or NotificationSetting.DisabledByGroupPolicy;
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
        if (!_isRegistered || Setting() != NotificationSetting.Enabled)
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
            var tag = Tag(id);
            _shown.Remove(tag);
            try
            {
                ToastNotificationManager.History.Remove(tag, SessionsGroup, AppId);
                ToastNotificationManager.History.Remove(tag, QuotaGroup, AppId);
            }
            catch (Exception error) when (IsSystemFailure(error))
            {
                DiagnosticLog.Append($"notifications: withdrawal failed: {error.Message}");
            }
        }
    }

    /// <summary>Settings ▸ System ▸ Notifications, where Claudio's switch is.</summary>
    public static void OpenSettings() => Shell.Open(new Uri("ms-settings:notifications"));

    /// <summary>At exit the banners left in the notification centre stay; their clicks go nowhere.</summary>
    public void Dispose()
    {
        _isRegistered = false;
        _shown.Clear();
        _notifier = null;
    }

    /// <summary>
    /// The tag is the alert's id, so posting the same event again replaces its banner and
    /// <see cref="Withdraw"/> finds it; the group is Claudy's thread.
    /// </summary>
    private void Post(Alert alert)
    {
        if (_notifier is null)
        {
            return;
        }
        try
        {
            var launch = new List<string>();
            if (alert.SessionId is { } session)
            {
                launch.Add($"{SessionArgument}={Uri.EscapeDataString(session)}");
            }
            if (alert.AgentId is { } agent)
            {
                launch.Add($"{AgentArgument}={Uri.EscapeDataString(agent)}");
            }
            var document = new XmlDocument();
            var toast = document.CreateElement("toast");
            toast.SetAttribute("launch", string.Join('&', launch));
            var visual = document.CreateElement("visual");
            var binding = document.CreateElement("binding");
            binding.SetAttribute("template", "ToastGeneric");
            foreach (var line in new[] { alert.Title, alert.Body })
            {
                var text = document.CreateElement("text");
                text.InnerText = line;
                binding.AppendChild(text);
            }
            visual.AppendChild(binding);
            toast.AppendChild(visual);
            document.AppendChild(toast);

            var tag = Tag(alert.Id);
            var notification = new ToastNotification(document)
            {
                Tag = tag,
                Group = alert.Kind.IsQuota() ? QuotaGroup : SessionsGroup,
            };
            notification.Activated += (_, args) => Dispatch(args is ToastActivatedEventArgs activated ? activated.Arguments : string.Empty);
            notification.Dismissed += (_, _) => _ui.TryEnqueue(() => _shown.Remove(tag));
            _shown[tag] = notification;
            _notifier.Show(notification);
        }
        catch (Exception error) when (IsSystemFailure(error))
        {
            DiagnosticLog.Append($"notifications: post failed: {error.Message}");
        }
    }

    /// <summary>An id longer than Windows takes is hashed, the same way each time.</summary>
    private static string Tag(string id) =>
        id.Length <= MaximumTag ? id : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)))[..32];

    private NotificationSetting Setting()
    {
        if (_notifier is null)
        {
            return NotificationSetting.DisabledForApplication;
        }
        try
        {
            return _notifier.Setting;
        }
        catch (Exception error) when (IsSystemFailure(error))
        {
            // Windows reads the switch of an app it finds a Start menu shortcut for: a copy run
            // from its build folder has none. Its banners still go out, and Windows itself drops
            // them if the user turned Claudio off: unknown reads as on, said once in the log.
            if (!_hasLoggedSetting)
            {
                _hasLoggedSetting = true;
                DiagnosticLog.Append($"notifications: setting unreadable (0x{error.HResult:X8}), taken as on");
            }
            return NotificationSetting.Enabled;
        }
    }

    /// <summary>Windows raises a click on a worker thread: the arguments go over to the UI thread.</summary>
    private void Dispatch(string launch)
    {
        var arguments = launch.Split('&', StringSplitOptions.RemoveEmptyEntries)
                              .Select(pair => pair.Split('=', 2))
                              .Where(pair => pair.Length == 2)
                              .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1]), StringComparer.Ordinal);
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
