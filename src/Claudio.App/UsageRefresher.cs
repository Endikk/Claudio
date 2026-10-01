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
    private readonly Action<CardSummary> _show;
    private bool _isRefreshing;

    public UsageRefresher(DispatcherQueue queue, Action<CardSummary> show)
    {
        _show = show;
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
            var payload = installed ? await _client.FetchAsync() : null;
            var card = CardSummary.From(payload, installed, DateTimeOffset.UtcNow);
            _show(card);
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
