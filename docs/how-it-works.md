# How Claudio works

[← README](../README.md)

Claudio reads Anthropic's answers exactly as [Claudy](https://github.com/Endikk/Claudy) does: both
apps run the same test cases (`claudy/Fixtures/`), and this page follows Claudy's own, with what
differs on Windows.

## Where the data comes from

| Data | Source |
|---|---|
| Gauge percentages and reset times | `api.anthropic.com/api/oauth/usage` |
| Tokens, models, projects, sessions | `<config>\projects\**\*.jsonl` — one `message.usage` object per response — and the same folder in every running WSL distribution |
| Account, plan, organisation | `api.anthropic.com/api/oauth/profile`, falling back to `.claude.json` (`oauthAccount` block) |
| Role (Admin badge) | `.claude.json`, `oauthAccount` block |
| Display name with no Claude account | The Windows user name |

**Invariant: the gauge percentages come from the API alone.** Transcripts only ever supply the
**token detail** (totals, splits, curve). The two are never merged into a single figure.

`<config>` resolves, in order: the `configDir` setting in `%LOCALAPPDATA%\Claudio\settings.json`,
then `CLAUDE_CONFIG_DIR`, otherwise `%USERPROFILE%\.claude`. A Windows app inherits the user's
environment variables, so the variable applies however Claudio starts. To redirect the
configuration for Claudio alone:

```json
{ "configDir": "D:\\my-claude-folder" }
```

When a custom directory is set there is no fallback to the home folder: redirecting the
configuration isolates completely.

Without one, transcripts are read from `%USERPROFILE%\.claude\projects` and also from
`%USERPROFILE%\.config\claude\projects`, where some Claude Code releases wrote. Claude Code run
inside WSL keeps its transcripts in the distribution's own file system: Claudio reads
`\\wsl.localhost\<distribution>\home\*\.claude\projects` for every **running** distribution only,
since opening a stopped one would start a whole virtual machine for nothing. A response found in
two folders counts once.

Counted tokens are the sum of the four counters (`input`, `output`, `cache_creation`,
`cache_read`). Cache reads dominate: a busy week routinely passes a billion tokens, hence the "B"
unit in the interface.

Claude Code writes the same response across several transcript lines, one per content block.
Claudio **deduplicates** on `(message.id, requestId)` and keeps the largest copy, which is the
final one: the early lines carry the output count reached so far. A resumed session copies earlier
responses into a new transcript: those are deduplicated the same way, across files, whatever order
the disk lists them in.

A project's name comes from the line's `cwd`, resolved to its Git repository (the nearest folder
holding `.git`, worktrees folded into their main checkout), never from the transcript folder name,
which loses accents and separators. `g:\work` and `G:\work` are one folder on Windows, and one
project.

### How the percentages stay true

**One figure, the account's.** The gauges show the percentages and reset times reported by
`api.anthropic.com/api/oauth/usage` — the endpoint behind claude.ai ▸ Settings ▸ Usage and Claude
Code's `/usage`. Session 5h, weekly across all models, weekly for the scoped model. Nothing is
recomputed, nothing is estimated.

**The token is borrowed from Claude Code, read-only.** On Windows, Claude Code keeps it in
`<config>\.credentials.json`. Claude Code renews it on every launch and before every expiry, so it
is always fresh and Claudio has nothing to refresh. Its `refresh_token` is not even kept in memory:
a rotation triggered by Claudio would invalidate Claude Code's own session.

Claudio's own sign-in ("Sign in to Claude") remains available for PCs where Claude Code is not
signed in, or not installed. It is the second choice: its token lives in the **Windows Credential
Manager** under `Claudio-credentials`, is refreshed on its own, and is abandoned as soon as
Anthropic answers `invalid_grant`. "Sign out" deletes it and stops Claudio reading Claude Code's
token until the user signs back in; Claude Code itself stays signed in.

What makes the link dependable:

- **Two sources, never an invention.** The API first; failing that, the counters Claude Code
  already received in its `anthropic-ratelimit-unified-*` headers, which a status-line command can
  drop for Claudio (see below).
- **A single retry on 401**: on a borrowed token it is re-read (Claude Code may have just written a
  new one); on Claudio's own it is refreshed. Never a loop.
- **Last known value plus backoff**: a failure resets nothing. The last reading stays on screen
  behind a dated "⟳" badge, and attempts space out — `Retry-After` honoured, otherwise 5 → 15 → 30
  → 60 min for a rate limit, and only 30 s → 5 min for a transient network or server fault. A
  refresh you ask for lifts the wait, once a minute at most.
- **Log**: every failure is timestamped in `%LOCALAPPDATA%\Claudio\api.log`, which is what
  separates a rate limit from a dead token.

These endpoints are undocumented and may change without notice. That is precisely why the app
never fills their silence.

**With no measurement, there is no figure.** The gauges show "—" and an "offline" badge. Tokens
counted in the transcripts are still displayed, but for what they are: **this PC's** consumption
("12.4 M tokens on this machine"), the 7-day history and the splits. They never mix with the
account percentage.

Unlike Claudy, Claudio has **no demo mode**: without Claude Code and without a sign-in, the card
asks to sign in and shows nothing it did not measure.

### Enterprise: a monthly spend cap

Enterprise plans are billed on usage: the API answers `five_hour: null` and `seven_day: null`, and
the quota is a monthly spend cap set by the organisation, carried in a `spend` block (or the older
`extra_usage`). The card then leads with **Spend · month**: the percentage worked out from the two
amounts, the amounts themselves ("$46.31 of $500.00") and the reset date on the 1st. The weekly and
per-model columns step aside. At the cap, the laptop explodes as it does at 100 % of a session.

### Status-line bridge (optional)

On every API response Claude Code reads its quota headers and passes them to the status line. One
command is enough to drop them where Claudio knows to read them — useful when the API is
momentarily unreachable. Claude Code runs status lines through Git Bash on Windows:

```bash
tee "$LOCALAPPDATA/Claudio/usage-bridge.json" > /dev/null
```

Add it to the `statusLine` command in `%USERPROFILE%\.claude\settings.json` (at the end of the
chain, so it does not disturb the existing display). Claudio ignores a reading older than 30
minutes, and only falls back to it when the API has produced nothing fresh.

### The pace marker

Each gauge carries a vertical line: the share of its window **already elapsed**. Halfway through a
5-hour session, steady consumption would sit right on the line.

- Fill **to the right** of the marker → ahead of the clock.
- Fill **to the left** → behind the pace.

The main block spells the gap out ("15 pts ahead of pace"), the columns reduce it to a sign
(`+15` / `−14`). Below a 4-point gap the app says "on pace". Past 20 points ahead, the label turns
red. The marker disappears when no window is running.

Past **95 %** on any **measured** gauge, the card's hairline turns red, at an intensity rising to
100 %. An unmeasured gauge never triggers it.

### Ports

The **ports** tab lists the TCP ports in the listening state that belong to processes of the
current user and that **Claude launched**: the process environment carries `CLAUDECODE`,
`CLAUDE_CODE_ENTRYPOINT` or `CLAUDE_PROJECT_DIR`, inherited from the session that started it. It is
read from the process's own memory (its environment block), and nothing but those markers ever
leaves it. A port is **live** while a Claude process is among its ancestors, **orphan** otherwise.
Container runtimes (Docker, WSL's relay…) are never listed.

Closing a port refuses whenever closing would be worse than leaving it open: a process whose start
time changed since the scan (a reused process number), Claudio itself and its ancestors, a running
Claude. Its children go first, then the process.

### Motion

The mascot types while a session runs, on the account or on this PC; explodes once when the
5-hour session, the weekly limit or the monthly cap fills while Claudio is on screen, and stays
dead until the quota frees up (opening Claudio on a full quota shows the dead state directly);
and waves while a new version is out, until Claudio is opened. With Windows' *Animation effects*
off (Settings ▸ Accessibility ▸ Visual effects), as with macOS's "Reduce motion" for Claudy, it
holds a still frame of each.

### Updates

Claudio looks for a new release at launch, once a day, and on any refresh you ask for (once a
minute at most); a beta follows the betas. A new version downloads in the background: a coral dot
marks the card and the icon, a line above the footer offers to restart into it, and it is applied
when Claudio quits anyway. A bubble pops up once per version, wherever Claudio lives: above the
clock in the notification area, against the card when it floats (above it, or below it near the
top of the screen), following it when it is dragged.

## Privacy

The network is used only to talk to Anthropic: `usage` (quotas), `profile` (account identity), and
— when you use Claudio's own sign-in — the OAuth flow in your browser plus the standard token
renewal. Claudio looks for its own updates on GitHub's releases. Nothing else is sent: no
telemetry, no conversation content, no third-party server.
