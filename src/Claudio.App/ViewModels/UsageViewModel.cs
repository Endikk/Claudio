using System.Diagnostics;
using Claudio.App.Views;
using Claudio.Core.Models;
using Claudio.Core.Services;
using Microsoft.UI.Dispatching;

namespace Claudio.App;

/// <summary>
/// The UI's single source of truth, as Claudy's <c>UsageViewModel</c>: the usage snapshot plus the
/// widget's preferences and the sign-in state. Every change raises <see cref="Changed"/> on the UI
/// thread, and the views redraw from it.
/// </summary>
internal sealed class UsageViewModel : IDisposable
{
    /// <summary>
    /// Three minutes: Anthropic's quotas move by the minute, and polling faster brought nothing
    /// but a cascade of 429s. Opening the card and waking the PC each trigger an immediate reading.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(3);

    /// <summary>
    /// While the card asks the user to sign in. Signing in to Claude Code must switch the card
    /// within seconds, and the reading costs nothing then: with no token there is no request.
    /// </summary>
    private static readonly TimeSpan SignInCheckInterval = TimeSpan.FromSeconds(10);

    private readonly ClaudeHome _home = Preferences.ConfigDirectory is { Length: > 0 } custom
        ? new ClaudeHome(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), custom)
        : ClaudeHome.Current;
    private readonly HttpClient _http = new();
    private readonly ClaudeAccountClient _client;
    private readonly ClaudeOAuth _oauth;
    private readonly LocalUsageDataSource _source;
    private readonly DispatcherQueueTimer _timer;
    private readonly DispatcherQueueTimer _wakeWatch;
    private DateTimeOffset _lastWakeTick = DateTimeOffset.UtcNow;
    private int _signInAttempt;
    private CardTab _tab;

    public UsageViewModel(DispatcherQueue ui)
    {
        var signedOut = new SignOutFlag(() => Preferences.IsSignedOut, value => Preferences.IsSignedOut = value);
        _client = new ClaudeAccountClient(_http, () => ClaudeCodeCredentials.Load(_home), ClaudeCredentialsStore.Store,
                                          signedOut, log: DiagnosticLog.Append);
        _oauth = new ClaudeOAuth(_http, uri => Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose(),
                                 DiagnosticLog.Append);
        // Windows' own folders, then every running WSL distribution's: a response found in two
        // places counts once.
        var scanner = new TranscriptScanner(() => [.. ProjectsDirectories(), .. WslSources.ProjectsDirectories()]);
        var accounts = new AccountLoader(_home);
        _source = new LocalUsageDataSource(scanner.Scan, _client.FetchAsync, () => _home.IsInstalled,
                                           now => UsageBridge.Read(BridgeFile, now), accounts.Load);

        _timer = ui.CreateTimer();
        _timer.IsRepeating = true;
        _timer.Tick += async (_, _) => await RefreshAsync();

        // A PC waking from sleep finds its timers late: a tick far behind the clock means the
        // machine slept, and the card must read the account again at once.
        _wakeWatch = ui.CreateTimer();
        _wakeWatch.Interval = TimeSpan.FromSeconds(30);
        _wakeWatch.IsRepeating = true;
        _wakeWatch.Tick += async (_, _) =>
        {
            var now = DateTimeOffset.UtcNow;
            var slept = now - _lastWakeTick > TimeSpan.FromSeconds(90);
            _lastWakeTick = now;
            if (slept)
            {
                await RefreshAsync();
            }
        };
        _wakeWatch.Start();

        IsMinimal = Preferences.IsMinimal;
        IsAlwaysOnTop = Preferences.IsAlwaysOnTop;
        IsDetailsExpanded = Preferences.IsDetailsExpanded;
        Placement = PlacementExtensions.Stored(Preferences.Placement);
        LaunchesAtLogin = LaunchAtLogin.IsEnabled;
    }

    /// <summary>Where Claude Code's status line can drop its counters for Claudio.</summary>
    public static string BridgeFile { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Claudio", "usage-bridge.json");

    public event Action? Changed;

    /// <summary>Called on every refresh the user asks for; the app looks for a new Claudio there.</summary>
    public Action? OnUserRefresh { get; set; }

    public UsageSnapshot Snapshot { get; private set; } = UsageSnapshot.Placeholder(DateTimeOffset.UtcNow);

    public bool IsRefreshing { get; private set; }

    /// <summary>False until the first reading: the card shows a loading state, no ghost onboarding.</summary>
    public bool HasLoaded { get; private set; }

    /// <summary>Last failure, null when all is well. The last valid snapshot stays on screen.</summary>
    public string? ErrorMessage { get; private set; }

    public bool IsSigningIn { get; private set; }

    /// <summary>The loopback port was taken: the page shows <c>code#state</c> for pasting into the card.</summary>
    public bool IsAwaitingManualCode { get; private set; }

    public bool IsProfileVisible { get; private set; }

    public bool IsSignedIn => Snapshot.IsSignedIn;

    public bool IsClaudeInstalled => _home.IsInstalled;

    public bool IsMinimal { get; private set; }

    public bool IsAlwaysOnTop { get; private set; }

    public bool IsDetailsExpanded { get; private set; }

    public Placement Placement { get; private set; }

    /// <summary>Deliberately not kept in the settings: the real state is the registry's.</summary>
    public bool LaunchesAtLogin { get; private set; }

    /// <summary>Which face of the card shows. Usage is the product; ports is an annex.</summary>
    public CardTab Tab
    {
        get => _tab;
        set
        {
            if (_tab != value)
            {
                _tab = value;
                Notify();
            }
        }
    }

    /// <summary>
    /// Takes a reading. <paramref name="userInitiated"/> lifts any backoff in progress: a click on
    /// "refresh" must attempt something, even mid-way through an hour-long wait after a 429.
    /// </summary>
    public async Task RefreshAsync(bool userInitiated = false)
    {
        if (userInitiated)
        {
            OnUserRefresh?.Invoke();
        }
        if (IsRefreshing)
        {
            return;
        }
        IsRefreshing = true;
        Notify();
        try
        {
            if (userInitiated)
            {
                _client.ResetBackoff();
            }
            Snapshot = await _source.FetchAsync();
            ErrorMessage = null;
        }
        catch (UsageDataException error)
        {
            ErrorMessage = error.Message;
        }
#pragma warning disable CA1031 // A failed reading is shown on the card; the last snapshot stays.
        catch (Exception error)
#pragma warning restore CA1031
        {
            DiagnosticLog.Append($"refresh failed: {error}");
            ErrorMessage = $"Refresh failed: {error.Message}";
        }
        finally
        {
            HasLoaded = true;
            IsRefreshing = false;
        }
        Schedule();
        Notify();
    }

    public void ToggleMode()
    {
        IsMinimal = !IsMinimal;
        Preferences.IsMinimal = IsMinimal;
        if (IsMinimal)
        {
            IsProfileVisible = false;
        }
        Notify();
    }

    public void Place(Placement placement)
    {
        IsProfileVisible = false;
        Placement = placement;
        Preferences.Placement = placement.ToString();
        Notify();
    }

    public void ToggleDetails()
    {
        IsDetailsExpanded = !IsDetailsExpanded;
        Preferences.IsDetailsExpanded = IsDetailsExpanded;
        Notify();
    }

    public void ToggleProfile()
    {
        IsProfileVisible = !IsProfileVisible;
        Notify();
    }

    public void SetAlwaysOnTop(bool onTop)
    {
        IsAlwaysOnTop = onTop;
        Preferences.IsAlwaysOnTop = onTop;
        Notify();
    }

    /// <summary>Applies the request, then realigns with the state actually reached.</summary>
    public void SetLaunchAtLogin(bool enabled)
    {
        LaunchesAtLogin = LaunchAtLogin.Set(enabled);
        Notify();
    }

    /// <summary>Claude Code's session is reused when it has one, with no browser; the browser sign-in runs only when it does not.</summary>
    public void StartSignIn()
    {
        if (IsSigningIn)
        {
            return;
        }
        IsSigningIn = true;
        ErrorMessage = null;
        var attempt = ++_signInAttempt;
        Notify();

        if (_client.ResumeWithClaudeCode())
        {
            IsSigningIn = false;
            _ = RefreshAfterSessionChangeAsync();
            return;
        }
        if (IsSigningIn && attempt == _signInAttempt)
        {
            BeginBrowserSignIn();
        }
    }

    private void BeginBrowserSignIn()
    {
        if (_oauth.Begin() == OAuthMode.Manual)
        {
            IsAwaitingManualCode = true;
            Notify();
            return;
        }
        _ = AwaitLoopbackAsync();
    }

    private async Task AwaitLoopbackAsync()
    {
        try
        {
            await CompleteSignInAsync(await _oauth.AwaitLoopbackCodeAsync(CancellationToken.None));
        }
#pragma warning disable CA1031 // Any failure ends the sign-in and is shown on the card.
        catch (Exception error)
#pragma warning restore CA1031
        {
            FailSignIn(error);
        }
    }

    public void SubmitManualCode(string pasted)
    {
        if (!IsAwaitingManualCode)
        {
            return;
        }
        _ = Redeem();

        async Task Redeem()
        {
            try
            {
                await CompleteSignInAsync(await _oauth.RedeemManualCodeAsync(pasted));
            }
#pragma warning disable CA1031 // Any failure ends the sign-in and is shown on the card.
            catch (Exception error)
#pragma warning restore CA1031
            {
                FailSignIn(error);
            }
        }
    }

    public void CancelSignIn()
    {
        _oauth.Cancel();
        IsSigningIn = false;
        IsAwaitingManualCode = false;
        Notify();
    }

    /// <summary>Claudio stops reading the quotas until the user signs back in. Claude Code stays signed in.</summary>
    public void SignOut()
    {
        _client.SignOut();
        IsProfileVisible = false;
        _ = RefreshAfterSessionChangeAsync();
    }

    private async Task CompleteSignInAsync(OAuthCredentials credentials)
    {
        _client.SignIn(credentials);
        IsSigningIn = false;
        IsAwaitingManualCode = false;
        await RefreshAfterSessionChangeAsync();
    }

    /// <summary>A refresh under way may have read the old session: wait for it, then read again.</summary>
    private async Task RefreshAfterSessionChangeAsync()
    {
        while (IsRefreshing)
        {
            await Task.Delay(50);
        }
        await RefreshAsync();
    }

    private void FailSignIn(Exception error)
    {
        // A sign-in the user cancelled ends quietly.
        if (!IsSigningIn && !IsAwaitingManualCode)
        {
            return;
        }
        IsSigningIn = false;
        IsAwaitingManualCode = false;
        ErrorMessage = error is OAuthException ? error.Message : $"Sign-in failed: {error.Message}";
        Notify();
    }

    /// <summary>Restarts the timer when the pace changes: signed in or not.</summary>
    private void Schedule()
    {
        var interval = HasLoaded && !IsSignedIn ? SignInCheckInterval : RefreshInterval;
        if (_timer.Interval != interval || !_timer.IsRunning)
        {
            _timer.Interval = interval;
            _timer.Start();
        }
    }

    /// <summary>
    /// <c>projects</c> under Claude Code's folder, and <c>~/.config/claude/projects</c>, where some
    /// releases kept their transcripts; a redirected configuration isolates completely.
    /// </summary>
    private List<string> ProjectsDirectories()
    {
        List<string> folders = [_home.ProjectsDirectory];
        if (_home.CustomConfigDirectory is null)
        {
            folders.Add(Path.Combine(_home.Home, ".config", "claude", "projects"));
        }
        return folders;
    }

    private void Notify() => Changed?.Invoke();

    public void Dispose()
    {
        _timer.Stop();
        _wakeWatch.Stop();
        _client.Dispose();
        _http.Dispose();
    }
}
