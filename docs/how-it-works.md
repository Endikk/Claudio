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

### Sessions and agents

The Sessions tab lists every Claude Code session open on this PC and the subagents each one runs,
from files Claude Code keeps anyway; nothing is installed in Claude Code for it:

- `%USERPROFILE%\.claude\sessions\<pid>.json`, one per open Claude Code process, gives each
  session its id, name and status, rewritten as it changes: `busy` while it works, `idle` at its
  prompt, `waiting` when it asks the user something, with what it waits for in `waitingFor`. A
  file whose process is gone belongs to a closed session and is ignored. Its `procStart` (a
  FILETIME on Windows) is checked against the process's own creation time, so a pid Windows has
  handed to another program is never taken for the session.
- `<projects>\<folder>\<session>.jsonl`, the session's transcript, for the last thing the user
  asked it (`last-prompt` lines) and its own last tool call; Claudio reads its tail once, then
  only what is appended.
- `<projects>\<folder>\<session>\subagents\agent-<id>.meta.json` holds what an agent was asked
  and, once it is over, `finished`; `agent-<id>.jsonl` beside it is its transcript, of which only
  the tool calls are read.

Sessions inside WSL distributions are not listed: their pids are Linux's, which Windows cannot
check.

Sessions waiting for the user come first, in an amber tile that names what they wait for and
offers "Open": it brings forward the app the session runs in, found up its process tree (Windows
Terminal, VS Code, Cursor, a JetBrains IDE…), or the console window a session was opened in.
Then come the sessions at work, then those at rest. Sessions a program drives through the Agent
SDK are listed only while they run agents.

A click unfolds a session: its folder, its app, when it opened, its agent count, the last thing
asked of it, "Open", and "Interrupt" while it works or waits. Agents have no process of their own,
so after a confirmation that names the session, Claudio sends the session what Ctrl+C sends in its
terminal: it attaches to the session's console for a moment and raises Ctrl+C there. The turn
stops and every agent of that session with it; the session stays open. Right before, the process
is held open and checked again against the session's start time and its owner.

Each agent sits under its session with its own Claudy, its colour kept while it runs, typing
while it works and grey after a minute of silence, and its track: its tool calls over the last ten
minutes, one bar per fifteen seconds, writes in coral. Three minutes of silence are called out in
amber; a finished agent stays ten minutes, marked done with the files it wrote. No file says how
far along an agent is, so no percentage is shown.

The short card above the clock and the open island put the waiting sessions first, then the
agents in short. Next to the clock, an amber hand joins the figure while a session waits, then the
lead agent's Claudy with the count. The island at rest puts the count in its left ear in the lead
agent's colour, or, while a session waits, a raised hand on glass turned amber; a click on the
ears opens that session's app.

### The agent room

"Open agent room", in the card's menu, the notification area's and the island's, "Room" in the
Sessions tab, and Ctrl+Alt+R from any app, open a window in the middle of the screen: one open
space per project, a project being the folder its sessions run in. Claudy sits at the centre desk
for the session; its agents sit around it, four areas of six desks. `Claudio.exe --room` opens it
at launch. The window has the card's glass and moves from anywhere like the card, but it is an
ordinary window: it does not stay above the others, it is listed in Alt+Tab, and Escape closes it.

What an agent does is read from each tool call of its transcript, never from a list of known
skills: the tool (Read is a search, Edit a write), the file written (`plan.md` under `specs\` is a
spec, a `.tf` file is infra) and, for a shell call, the command. A line that chains commands is
named by its most telling one: `cd app && npm test` is tests. A skill or an MCP server nobody has
heard of shows under its own name, and the commands it runs are read like any other. A command
whose program says nothing is "terminal".

Each kind of work belongs to one of three families, and each family has an area: writing,
commands, research. The fourth area is spare. An agent sits in the area of the family most of its
calls of the last ninety seconds fall in, so that one read between two edits does not send it
across the room, and stays put twenty seconds after a move. When an area is full, an empty one
takes the same work until it empties. Past twenty-four agents, the last ones have no desk.

A screen shows the symbol of its agent's last call (Claudy's SF Symbols, drawn with their Segoe
Fluent Icons counterparts), amber for a session waiting on the user (on Claudy's own screen), red
for an agent silent for three minutes, green for one that is done. Claudy's first screen shows
the session's own last tool call, read from the tail of its transcript like its last prompt, while
that call is less than two minutes old; otherwise that it runs its agents, or that it thinks.
Claudy's second screen draws the session quota as the card's ring; a click on it opens a summary
in sentences: what was asked, what happens now, the limit, "Open" and "Interrupt" as the Sessions
tab offers them. A click on a desk opens its agent's card: what it does now, its calls before
that, the files it wrote. An agent silent for a minute, waiting on a long command or on its
parent, dozes at its desk.

An agent that is done leaves its desk for the sofa, four places, and walks out by the door when it
leaves the list; with the sofa full it goes straight out. A Claudy leaves and reaches its desk by
the aisle behind its row, so it never walks through its neighbours. Past three quarters of the
session quota the racks' lights turn amber, past nine tenths red, with a red glow at the room's
edges. Nobody walks in for a room that was already at work when it opened.

The window is drawn from the hour alone: the sky blends from one moment of the day to the next, the
sun crosses it from six to six and stands in the middle at noon, the moon does the same by night,
and each window of the city has a fixed threshold that decides when it lights up, so the same ones
come on first every evening: a third of them at half past five, nearly all at nine, a fifth at
midnight. A window of the city fades in and out rather than switching. The hour is the PC's own,
in the window as on the wall clock: there is nothing to set. The weather asks nothing of the
network: each day has its own, worked out from its date. Half the days are clear, two in ten
overcast, three in ten wet, and from December to February what falls is mostly snow. The room's
menu can hold one weather instead.

With more than one project open, "All" lays every project out as a small room, drawn once a
second; a click enters one. A room is named after its sessions' folder, whatever opened them: a
conversation of the Claude desktop app works in a folder of its own, `scratch-` and a date, and
gets a room like any other. A cross beside a room's name, under the pointer, puts the room away. It
comes back from the room's menu, when a banner leads to one of its agents, or once its sessions
have closed and the folder is opened again; until then it stays away across launches. A click on
a banner about an agent opens the room of its project with its card open; a banner about a session
still brings the session's app forward.

What never moves (floor, walls, furniture, a desk's own furniture, the symbols of the screens) and
the words on the signs are drawn once and kept. The rest is redrawn thirty times a second, so that
the vacuum glides and a Claudy walks without a jolt. The window's sky and city are redrawn five
times a second, and what falls in front of them thirty. Nothing is drawn once the room is closed,
or while its window is hidden or minimised.

### Keyboard shortcuts

Ctrl+Alt+C, from any app, shows or hides Claudio where it stands: the card comes and goes, the
short card opens and closes, the island opens and closes (a click elsewhere closes it again).
Ctrl+Alt+R opens or closes the agent room. They go through `RegisterHotKey`, which needs no
permission; when another app already holds one, the menu says so rather than promising it. They
are Claudy's ⌃⌥C and ⌃⌥R.

### MCP servers left behind

Claude Code runs MCP servers as child processes that talk over their standard input, with no
port, so the ports list never saw them. When a session ends badly they stay behind with no
parent, often holding hundreds of megabytes each. The ports tab lists them under the ports, with
the memory each tree holds (its working set) and the total, and kills them like an orphaned port.

A process is listed when all of this holds:

- it is yours, its command line names an MCP server (`chrome-devtools-mcp`, `mcp-server-fetch`,
  `@modelcontextprotocol/...`, through `cmd /c npx`, `node.exe` or an npm `.cmd` shim alike), and
  its program is not an installed app's own (an MSIX package under `WindowsApps`, a per-user
  `app-1.2.3` install, an Electron app's `resources\app`). `Program Files` alone does not count:
  Node and Python live there;
- its parent is gone: no process holds its parent's number, or the one that does started after
  it, since Windows hands numbers out again;
- its environment carries Claude Code's markers (`CLAUDECODE`, `CLAUDE_PROJECT_DIR`), and its
  `CLAUDE_CODE_SESSION_ID`, when it has one, names no open session;
- no Claude Code process sits above it, and it does not hold a port already listed.

Only processes that pass the cheap checks get their environment read, and nothing but those
three markers is kept from it. A daemon a plugin keeps on purpose, and the servers it runs
itself, are not listed while that daemon lives.

A kill goes to the whole tree, deepest first and the server last, then once more to whatever
still runs, after checking again that the server is the one listed (its start time) and that no
live Claude Code sits above or below it. Windows has no process group: nothing outside the tree
the server heads is touched.

### Notifications

Claudio posts a Windows notification when something deserves a look. Each kind has its own
switch, as in Claudy:

| Kind | When | Default |
|---|---|---|
| Session at 80% | The 5-hour session crosses 80 %, once per window | on |
| Limit before the reset | At the pace so far, the session fills more than ten minutes before it resets; said once per window, from 50 % on | on |
| Session back | A session window seen full has reset | on |
| A session waits for you | A session has waited twenty seconds for a permission or an answer; the banner is taken back once it no longer waits | on |
| An agent falls silent | An agent has said nothing for three minutes | on |
| An agent is done | An agent seen at work finishes | off |

The first reading after launch only sets the scene: a threshold already crossed then is not
announced. Windows asks no permission; if notifications are turned off for Claudio in Settings,
the menu says so and leads there. The demo set never posts anything.

### CSV export

The export writes the last seven days from the local transcripts, one line per hour, project and
model, in the very columns Claudy writes:

```
hour,project,model,input_tokens,output_tokens,cache_read_tokens,cache_write_tokens,list_price_usd
2026-11-10T09:00,Claudy,claude-opus-5-5,18204,9310,2210440,86020,4.1200
```

`list_price_usd` is what those tokens would cost at Anthropic's list prices, not a bill. Hours are
local, dates Gregorian and numbers written with a point whatever the system's language. A field
that a spreadsheet would read as a formula is prefixed with `'`. Without Claude Code there is
nothing to export: the demo set is never written out.

### Motion

The mascot types while a session runs, on the account or on this PC; explodes once when the
5-hour session, the weekly limit or the monthly cap fills while Claudio is on screen, and stays
dead until the quota frees up (opening Claudio on a full quota shows the dead state directly);
and waves while a new version is out, until Claudio is opened. With Windows' *Animation effects*
off (Settings ▸ Accessibility ▸ Visual effects), as with macOS's "Reduce motion" for Claudy, it
holds a still frame of each.

### The island

Claudy can live round a MacBook's notch: two ears hugging it at rest, the mascot on the left and the
session's figure on the right, which drop open on hover into the session's bar, its pace and reset,
and the other quotas. No PC has a notch, so Claudio hangs the same island from the top centre of the
main screen, round a gap of its own between the ears (`--simulate-notch 185x32` gives it a
MacBook's). It opens after a pointer has rested on it for 0.15 s, so a pointer crossing it on its way
to a tab or a title bar leaves it shut, and closes 0.3 s after the pointer leaves; the icon next to
the clock opens and closes it too. It never takes the focus, and steps aside while a game, a video
or a presentation runs full screen, where Claudy's sits in the black band the notch leaves.

### Updates

Claudio looks for a new release at launch, once a day, and on any refresh you ask for (once a
minute at most); a beta follows the betas. A new version downloads in the background: a coral dot
marks the card and the icon, a line above the footer offers to restart into it, and it is applied
when Claudio quits anyway. A bubble pops up once per version, wherever Claudio lives: above the
clock in the notification area, against the card when it floats (above it, or below it near the
top of the screen), following it when it is dragged, and under the island at the top of the screen.

### Demo mode

With no trace of Claude Code on the PC (no `.claude.json` and no `projects` folder, in Windows or in
a running WSL distribution) and nobody signed in to Claudio, the card switches to
`DemoUsageDataSource`, Claudy's sample set, and **says so** through a "demo" badge in the header.
The set contains nothing identifying: the name comes from the Windows session and the projects carry
neutral names. The switch is re-evaluated on every refresh, so installing Claude Code afterwards is
enough. Unlike Claudy, which has no other way in, Claudio's own sign-in stays offered on the demo
set, and an account signed in that way always wins over it.

Any other failure (unreadable `projects` folder, missing permissions) does **not** trigger demo
mode: the last valid reading stays on screen and an "error" badge appears, with the detail on hover.

## Privacy

The network is used only to talk to Anthropic: `usage` (quotas), `profile` (account identity), and
— when you use Claudio's own sign-in — the OAuth flow in your browser plus the standard token
renewal. Claudio looks for its own updates on GitHub's releases. Nothing else is sent: no
telemetry, no conversation content, no third-party server.
