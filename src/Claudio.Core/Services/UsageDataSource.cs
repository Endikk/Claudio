using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>
/// No trace of Claude Code on this PC, and nobody signed in to Claudio: Claudy's
/// <c>UsageDataError.claudeNotInstalled</c>, which hands over to the demo set.
/// </summary>
public sealed class ClaudeNotInstalledException() : Exception("Claude Code is not installed on this PC.");

/// <summary>
/// The real source, as Claudy's <c>LocalUsageDataSource</c>: the account's quotas for the gauges,
/// Claude Code's local transcripts for the token detail. The account reading comes first; failing
/// that, the counters Claude Code relayed through its status line.
/// </summary>
public sealed class LocalUsageDataSource
{
    private readonly Func<IReadOnlyList<TranscriptEntry>> _scan;
    private readonly Func<CancellationToken, Task<AccountPayload>> _fetchAccount;
    private readonly Func<bool> _isInstalled;
    private readonly Func<DateTimeOffset, QuotaReading?> _bridge;
    private readonly Func<Account> _account;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeZoneInfo _zone;
    private readonly Lock _gate = new();

    /// <summary>
    /// Set once a pass over the history has ended, read or failed. From then on each reading waits
    /// for the history, so a read error reaches the card as it always did.
    /// </summary>
    private bool _hasReadHistory;

    /// <summary>The pass under way, joined rather than started again by a reading that comes meanwhile.</summary>
    private Task<IReadOnlyList<TranscriptEntry>>? _pass;

    public LocalUsageDataSource(Func<IReadOnlyList<TranscriptEntry>> scan,
                                Func<CancellationToken, Task<AccountPayload>> fetchAccount,
                                Func<bool> isInstalled,
                                Func<DateTimeOffset, QuotaReading?> bridge,
                                Func<Account> account,
                                Func<DateTimeOffset>? now = null,
                                TimeZoneInfo? zone = null)
    {
        _scan = scan;
        _fetchAccount = fetchAccount;
        _isInstalled = isInstalled;
        _bridge = bridge;
        _account = account;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _zone = zone ?? TimeZoneInfo.Local;
    }

    /// <summary>Any trace of Claude Code on this PC, in Windows or in a WSL distribution.</summary>
    public bool IsInstalled => _isInstalled();

    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancel = default)
    {
        // The history is read alongside the account rather than before it: a first pass over a
        // large one takes a moment, and who is signed in is known in milliseconds.
        var installed = _isInstalled();
        var scan = installed ? ReadHistory() : null;
        var payload = await _fetchAccount(cancel).ConfigureAwait(false);

        // Only the absence of Claude Code justifies the demo set, as in Claudy. Claudio's own
        // sign-in reads the account without Claude Code, and then the account wins.
        if (!installed && !payload.IsSignedIn)
        {
            throw new ClaudeNotInstalledException();
        }
        scan ??= ReadHistory();
        var now = _now();

        // Signed out on purpose means no quota at all: the card would otherwise keep a percentage
        // relayed by Claude Code's status line next to a card saying "not signed in".
        var bridge = payload.IsSignedOutByUser ? null : _bridge(now);
        var reading = UsageBridge.Merge(payload.Reading, bridge);

        var account = _account();
        if (payload.Profile is { } profile)
        {
            account = new Account(profile.Name, profile.Email, profile.Plan, profile.Organization, account.IsAdmin);
        }

        // The sign-in card shows no token count, so it never waits for the history. It takes it
        // once a pass has finished, which keeps the tray icon typing on local activity.
        IReadOnlyList<TranscriptEntry> entries = payload.IsSignedIn || HasReadHistory ? await scan.ConfigureAwait(false) : [];
        return UsageAggregator.Snapshot(entries, account, reading, _now(), _zone) with { IsSignedIn = payload.IsSignedIn };
    }

    private bool HasReadHistory
    {
        get
        {
            lock (_gate)
            {
                return _hasReadHistory;
            }
        }
    }

    private Task<IReadOnlyList<TranscriptEntry>> ReadHistory()
    {
        lock (_gate)
        {
            if (_pass is { } running)
            {
                return running;
            }
            var pass = Task.Run(() =>
            {
                try
                {
                    return _scan();
                }
                finally
                {
                    lock (_gate)
                    {
                        _hasReadHistory = true;
                        _pass = null;
                    }
                }
            });
            _pass = pass;
            return pass;
        }
    }
}

/// <summary>
/// The switch, as Claudy's <c>AdaptiveUsageDataSource</c>: real data when Claude Code is present
/// or someone signed in, the demo set otherwise. The choice is remade on every refresh, so
/// installing Claude Code, or signing in, is enough.
/// </summary>
public sealed class AdaptiveUsageDataSource(LocalUsageDataSource local, DemoUsageDataSource demo)
{
    /// <summary>
    /// Only the absence of Claude Code justifies the demo set: any other failure must surface on
    /// the card rather than show invented figures.
    /// </summary>
    public async Task<UsageSnapshot> FetchAsync(CancellationToken cancel = default)
    {
        try
        {
            return await local.FetchAsync(cancel).ConfigureAwait(false);
        }
        catch (ClaudeNotInstalledException)
        {
            return await demo.FetchAsync(cancel).ConfigureAwait(false);
        }
    }
}
