using System.Diagnostics;
using System.Reflection;
using Microsoft.UI.Dispatching;
using Velopack;
using Velopack.Sources;

namespace Claudio.App;

internal enum UpdateState
{
    Idle,
    Downloading,
    Ready,
    Restarting,
    Failed,
}

/// <summary>
/// Looks for a newer Claudio among the GitHub releases, as Claudy's <c>UpdateChecker</c>: at launch,
/// once a day, and on any refresh the user asks for (once a minute at most). A new version
/// downloads in the background, the card offers to restart into it, and it is applied when Claudio
/// quits anyway. While one is out, the mascot waves until the card is clicked. Silent on any
/// failure: no network means no update, never an error. A copy run from a build folder does nothing.
/// </summary>
internal sealed class UpdateChecker : IDisposable
{
    private const string Repository = "https://github.com/Endikk/Claudio";
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly TimeSpan ManualCheckSpacing = TimeSpan.FromMinutes(1);

    private readonly DispatcherQueue _ui;
    private readonly DispatcherQueueTimer _timer;
    private UpdateManager? _manager;
    private UpdateInfo? _update;
    private DateTimeOffset _lastCheck = DateTimeOffset.MinValue;
    private bool _isChecking;

    /// <summary>A simulated release is announced at every launch: it is never remembered.</summary>
    private bool _isSimulated;

    public UpdateChecker(DispatcherQueue ui)
    {
        _ui = ui;
        _timer = ui.CreateTimer();
        _timer.Interval = CheckInterval;
        _timer.IsRepeating = true;
        _timer.Tick += (_, _) => _ = CheckAsync();
    }

    public event Action? Changed;

    /// <summary>The newer version, once one is out.</summary>
    public string? Available { get; private set; }

    public UpdateState State { get; private set; }

    /// <summary>A new version is out and the card has not been clicked since: the mascot waves.</summary>
    public bool IsGreeting { get; private set; }

    /// <summary>From the moment a release is found until its bubble has been answered once.</summary>
    public bool ShouldAnnounce { get; private set; }

    /// <summary>
    /// The running version. A pre-release follows pre-releases, so a beta tester gets the next beta;
    /// a stable copy only ever sees stable releases.
    /// </summary>
    public static string Current { get; } =
        typeof(UpdateChecker).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "0.0.0";

    public void Start()
    {
#if DEBUG
        // Debug builds only, as Claudy's `-ClaudySimulateUpdate 1.5.3`: `--simulate-update 1.0.1`
        // pretends that version is out and downloaded, to see the dot, the wave and the bubble.
        if (Simulated() is { } version)
        {
            _isSimulated = true;
            Available = version;
            State = UpdateState.Ready;
            IsGreeting = true;
            ShouldAnnounce = true;
            Changed?.Invoke();
            return;
        }
#endif
        _timer.Start();
        _ = CheckAsync();
    }

#if DEBUG
    private static string? Simulated()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, "--simulate-update");
        return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
    }
#endif

    /// <summary>A refresh the user asked for also looks for a new Claudio, once a minute at most.</summary>
    public void CheckNow()
    {
        if (DateTimeOffset.UtcNow - _lastCheck >= ManualCheckSpacing)
        {
            _ = CheckAsync();
        }
    }

    public void AcknowledgeGreeting()
    {
        if (IsGreeting)
        {
            IsGreeting = false;
            Changed?.Invoke();
        }
    }

    /// <summary>The bubble was seen and closed: it does not come back for this version.</summary>
    public void MarkAnnounced()
    {
        ShouldAnnounce = false;
        if (Available is { } version && !_isSimulated)
        {
            Preferences.AnnouncedUpdate = version;
        }
        Changed?.Invoke();
    }

    /// <summary>Moving Claudio elsewhere greets again while an update is pending.</summary>
    public void GreetAgain()
    {
        if (Available is not null && !IsGreeting)
        {
            IsGreeting = true;
            Changed?.Invoke();
        }
    }

    /// <summary>Restarts into the downloaded version.</summary>
    public void Update()
    {
        if (_isSimulated)
        {
            // A simulated update installs nothing: taken at its word.
            DiagnosticLog.Append($"simulated update to {Available}");
            return;
        }
        if (_manager is null || _update is null || State != UpdateState.Ready)
        {
            return;
        }
        State = UpdateState.Restarting;
        Changed?.Invoke();
        try
        {
            _manager.ApplyUpdatesAndRestart(_update.TargetFullRelease);
        }
#pragma warning disable CA1031 // An update must never take Claudio down.
        catch (Exception)
#pragma warning restore CA1031
        {
            State = UpdateState.Failed;
            Changed?.Invoke();
        }
    }

    /// <summary>Quitting with a version downloaded applies it, never under the user's feet.</summary>
    public void ApplyOnExit()
    {
        if (_manager is null || _update is null || State != UpdateState.Ready)
        {
            return;
        }
        try
        {
            _manager.WaitExitThenApplyUpdates(_update.TargetFullRelease, silent: true, restart: false);
        }
#pragma warning disable CA1031 // An update must never keep Claudio from quitting.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    public static void OpenReleasePage() =>
        Process.Start(new ProcessStartInfo($"{Repository}/releases") { UseShellExecute = true })?.Dispose();

    /// <summary>"What's new": the notes of the version on offer.</summary>
    public void OpenReleaseNotes() =>
        Process.Start(new ProcessStartInfo(Available is { } version ? $"{Repository}/releases/tag/v{version}" : $"{Repository}/releases")
        { UseShellExecute = true })?.Dispose();

    private async Task CheckAsync()
    {
        if (_isChecking)
        {
            return;
        }
        _isChecking = true;
        _lastCheck = DateTimeOffset.UtcNow;
        try
        {
            _manager ??= new UpdateManager(new GithubSource(Repository, accessToken: null, prerelease: Current.Contains('-', StringComparison.Ordinal)));
            if (!_manager.IsInstalled)
            {
                return;
            }
            var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null || update.TargetFullRelease.Version.ToString() == Available)
            {
                return;
            }
            _update = update;
            var version = update.TargetFullRelease.Version.ToString();
            Post(() =>
            {
                Available = version;
                State = UpdateState.Downloading;
                IsGreeting = true;
                ShouldAnnounce = Preferences.AnnouncedUpdate != version;
            });
            await _manager.DownloadUpdatesAsync(update).ConfigureAwait(false);
            Post(() => State = UpdateState.Ready);
        }
#pragma warning disable CA1031 // An update check must never take Claudio down.
        catch (Exception)
#pragma warning restore CA1031
        {
            if (Available is not null)
            {
                Post(() => State = UpdateState.Failed);
            }
        }
        finally
        {
            _isChecking = false;
        }
    }

    private void Post(Action change) => _ui.TryEnqueue(() =>
    {
        change();
        Changed?.Invoke();
    });

    public void Dispose() => _timer.Stop();
}
