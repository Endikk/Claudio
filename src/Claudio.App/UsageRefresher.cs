using Claudio.Core.Presentation;
using Claudio.Core.Services;
using Microsoft.UI.Dispatching;

namespace Claudio.App;

/// <summary>
/// Reads the account at launch, then every three minutes as Claudy does: Anthropic's quotas move
/// by the minute, and polling faster only earns rate limits. While nobody is signed in it looks
/// again every ten seconds, which costs no request: there is no token to send.
/// </summary>
internal sealed class UsageRefresher : IDisposable
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan SignInInterval = TimeSpan.FromSeconds(10);

    private readonly ClaudeAccountClient _client;
    private readonly ClaudeHome _home = ClaudeHome.Current;
    private readonly DispatcherQueueTimer _timer;
    private readonly Action<CardSummary, TokenTotals> _show;
    private readonly TranscriptScanner _scanner;
    private TokenTotals _totals = TokenTotals.Empty;
    private bool _isRefreshing;

    public UsageRefresher(DispatcherQueue queue, Action<CardSummary, TokenTotals> show)
    {
        _show = show;
        // Windows' own folder, then every running WSL distribution's: a response found in two
        // places counts once.
        _scanner = new TranscriptScanner(() => [_home.ProjectsDirectory, .. WslSources.ProjectsDirectories()]);
        _client = new ClaudeAccountClient(new HttpClient(), () => ClaudeCodeCredentials.Load(_home), log: DiagnosticLog.Append);
        _timer = queue.CreateTimer();
        _timer.Tick += async (_, _) => await RefreshAsync();
    }

    public async Task RefreshAsync(bool userInitiated = false)
    {
        if (_isRefreshing)
        {
            return;
        }
        _isRefreshing = true;
        try
        {
            if (userInitiated)
            {
                _client.ResetBackoff();
            }
            var installed = _home.IsInstalled;
            // The history is read alongside the account, off the UI thread: a first pass over a
            // large one takes a moment, and who is signed in is known at once.
            var history = Task.Run(() => _scanner.Scan());
            var payload = installed ? await _client.FetchAsync() : null;
            _show(CardSummary.From(payload, installed, DateTimeOffset.UtcNow), _totals);
            try
            {
                _totals = TokenTotals.From(await history, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
            }
            catch (IOException)
            {
            }
            _show(CardSummary.From(payload, installed, DateTimeOffset.UtcNow), _totals);
            Schedule(payload?.IsSignedIn == true || !installed ? Interval : SignInInterval);
        }
        finally
        {
            _isRefreshing = false;
        }
    }

    private void Schedule(TimeSpan interval)
    {
        if (_timer.Interval != interval || !_timer.IsRunning)
        {
            _timer.Interval = interval;
            _timer.IsRepeating = true;
            _timer.Start();
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _client.Dispose();
    }
}
