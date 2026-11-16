# Changelog

All notable changes to this project are documented here. Dates are release dates.

## 1.6.0 (16 November 2026)

Claudio catches up with Claudy 1.6.0: the same features, on Windows, under the same version
number from now on.

### Added

- **Agent room.** "Open agent room" opens a window in the middle of the screen: one open space
  per project, Claudy at the centre desk for the session, its agents at the desks around, in the
  area of their work (writing, commands, research, and a spare area that takes the overflow).
  Each screen shows what its agent does, read from the tool, the file and the command of its
  last call: code, docs, spec, design, search, review, web, tests, build, benchmark, install,
  git, data, infra, deploy, security, or the name of the skill or MCP server at work. An agent
  whose work changes walks to another desk. The window tells the hour: the sun in the middle at
  noon, the city's windows lighting up as it sets, nearly all lit at nine, going out towards
  midnight. The room follows the PC's clock. A robot vacuum leaves its dock by the back wall,
  goes round the corridors and comes back to charge. Claudy's first screen shows what the
  session itself does between two agents. A click on a desk opens its agent's card: what it was
  asked, what it does, its last calls, the files it wrote. An agent that is done goes and sits on
  the sofa, and leaves by the door when it leaves the list. Past 90 % of the session quota the
  racks turn red and the room's edges glow. A click on Claudy's second screen opens a summary of
  the session in sentences, with Open and Interrupt.
- **More of the agent room.** Ctrl+Alt+R opens it from any app, and `Claudio.exe --room` opens it
  at launch. With several projects open, "All" shows every one as a small room, a click enters
  it. A banner about an agent, silent or done, opens the room on its desk. An agent that waits
  dozes at its desk. The window has weather: each day its own, decided by its date, rain some
  days and snow in winter, or the one chosen in the room's menu. A cross beside a room's name,
  under the pointer, puts the room away; the room's menu brings it back.
- **Sessions.** A third tab lists every Claude Code session open on the PC: the ones waiting for
  an answer first, in amber, with what they wait for ("Permission prompt"); then those at work,
  then those at rest with how long they have been idle. Each session shows the subagents it runs
  underneath: what each was asked, for how long, the file it is touching and its last ten
  minutes as a track, with its own Claudy in its own colour. Read from Claude Code's own files,
  nothing to install. Sessions a program drives through the Agent SDK only show while they run
  agents. Sessions inside WSL are not listed: Windows cannot check their processes.
- **Open a session's window.** "Open" brings forward the app a session runs in (Windows
  Terminal, VS Code, Cursor, a JetBrains IDE, or the console it was opened in), found up its
  process tree. A session unfolds to its folder, its app, the last thing asked of it, and the
  buttons.
- **Interrupt a session from the card.** After a confirmation that names the session, Claudio
  sends its console Ctrl+C, which stops the turn and every agent of that session and leaves the
  session open. The process is held and checked against the session's recorded start time first,
  so a pid reused by another process is never signalled.
- **Agents that stall, agents that are done.** Three minutes without a word and the row says so,
  in amber: "silent 4 min". A finished agent stays ten minutes with the number of files it wrote.
- **A session waiting for you shows everywhere.** The open island and the short card above the
  clock put it first, with "Open"; the island at rest turns amber with a raised hand, and a click
  on the ears opens the session's app; next to the clock, an amber hand joins the figure, and the
  lead agent's Claudy with the count while agents run.
- **Notifications.** Session at 80 %, the limit coming before the reset at the current pace, the
  session back after a full window, a session waiting for you, an agent gone silent, an agent
  done (off by default). One switch each under Notifications in the right-click menu. A click on
  a banner about a session brings its app forward; a waiting banner is taken back once the
  session moves on.
- **Ctrl+Alt+C shows or hides Claudio** from any app: the card, the short card or the island,
  wherever it stands. When another app holds the shortcut, the menu says so.
- **MCP servers left behind.** The Ports tab lists the MCP servers a closed Claude Code session
  left running, with the memory each one holds, and kills them like an orphaned port.
- **Export usage as CSV.** The last seven days from the local transcripts, one line per hour,
  project and model: the four token counts and what they weigh at list price. Never the demo set.
- **The short lists say more.** The short card and the island list four agents with their state
  in colour, and "+N more" opens the widget. The island at rest shows the count alone, in the
  lead agent's colour.

### Changed

- **Claudy's new look.** Windows has no Liquid Glass: the card takes Claudy's layout on its
  window material under a warm veil, so the text stays readable over any wallpaper. The session,
  the figure you come for, sits in a coral tile of its own; the weekly and per-model windows and
  the week's curve lie on the glass, apart by a hairline. A session waiting for you is the only
  other block.
- **One colour per window.** Coral for the session, sky for the week, violet for Sonnet, teal for
  Haiku. Amber and red no longer name a window: a gauge only turns amber or red when it nears its
  limit.
- **Easier to read.** Labels in sentence case instead of spaced capitals, no text under 11 points,
  three text tones, ports and file names in monospace, no more glows, gradient figures or pills
  around every count. The week's totals now sit in the curve's header.
- **The icon sits on a cream tile**, as Claudy's.
- **Release notes.** Each GitHub release now carries its section of this changelog.

### Fixed

- **Claudio at rest costs less.** The mascot stops moving in a window nobody sees: a hidden
  card, a closed short card, the agent room once closed. The icon next to the clock is only
  redrawn when its frame changes and types at half the pace, and the wave is redrawn at each of
  its frames instead of thirty times a second.
- **A damaged Claude Code file costs one value, not the list.** A date no date can hold, or half
  an emoji cut in a transcript, no longer stops the sessions from being read.

## 1.0.3 (7 October 2026)

### Fixed

- **The demo's session no longer starts 35 points ahead of its clock.** It opened at the moment
  of the reading, so its pace marker sat at zero under a third-full bar and the card read
  "35 pts ahead of pace" in red, for good. It now opens partway through its window, with as much
  of the window gone as of the quota used, so the gauge starts on its pace marker, as in Claudy.

## 1.0.2 (6 October 2026)

### Fixed

- **Claudio no longer closes when it starts before the notification area is ready.** Launched at sign-in before
  Explorer had drawn the taskbar, or in a session without one, the icon next to the clock failed to
  be created on its library's own thread, which ended the process. The icon now waits for the
  taskbar and Claudio shows its card or island meanwhile. Found by running the ARM64 build, which CI
  now builds and starts on an ARM machine.

## 1.0.1 (6 October 2026)

### Fixed

- **A browser that will not open no longer freezes sign-in.** Without a default browser, "Sign in"
  left Claudio waiting for ever and refusing to try again; it now says so on the card and lets you
  retry. The same goes for "What's new" and "Download".
- **A damaged settings file can no longer keep Claudio from starting or lose your choices.** A
  number where text belongs, half a file, an empty one: Claudio reads the defaults, keeps the
  damaged file as `settings.json.bad`, and saves a new one whole (written beside, then swapped in),
  so a crash or a power cut halfway never leaves half of it.
- **Failures are written down and survived.** A failure in a timer or a view is logged to `api.log`
  and Claudio carries on; a start that fails leaves a trace and closes, instead of leaving a
  process with nothing on screen. Update checks that fail are logged too.
- **The weekly sync with Claudy no longer fails on a release without shared data.** Claudy's v1.5.7
  has no `Design/` or `Fixtures/`; the sync now says so and changes nothing.

### Added

- **Every build is started for real before it ships.** CI and the release run the published Claudio
  once per placement (card, notification area, island) and fail if it exits, shows the wrong
  window, or logs a failure: a build like 1.0.0-beta.1, which closed at launch, can no longer be
  released.

## 1.0.0 (2 October 2026)

The first stable release: 1.0.0-beta.4, out of beta. Copies installed from a beta update to it, and
from here on Claudio follows the stable releases only.

## 1.0.0-beta.4 (2 October 2026)

### Added

- **The island at the top of the screen.** Claudy's third place, its island round the notch, on
  Windows: a dark band hangs from the top centre of the main screen, the mascot on one side and the
  session's figure on the other. Rest the pointer on it and it drops open, as Claudy's does, into
  the session's bar, its pace and reset, the weekly and per-model quotas and the update line, the
  mascot and the figure flying into place; it closes once the pointer leaves. Right-click it for
  Claudy's menu; the icon next to the clock opens it too. It steps aside while a game or a video
  runs full screen. Pick it from any menu: "Show at the top of the screen".
- **Claudy's demo mode.** On a PC without Claude Code where nobody signed in, the card shows
  Claudy's sample set instead of an empty sign-in card, labelled "demo" in the header, with nothing
  identifying in it. Signing in to Claudio, or installing Claude Code, brings the real figures back.

## 1.0.0-beta.3 (2 October 2026)

### Added

- **The update bubble, whatever the placement.** A new version now pops up on the floating card
  too, not only next to the clock: the waving mascot, the version, "What's new", Later or
  Update, just above the card (below it near the top of the screen). It follows the card when
  it is dragged and, once answered, does not come back for that version.

### Fixed

- **A dropped card stays where it was dropped.** The card went back to its corner whenever its
  measured height moved, even by a few pixels after a refresh. It now goes back only when it
  changes shape (full or compact, tab, details, update line), as Claudy's does.

## 1.0.0-beta.2 (1 October 2026)

Everything Claudy does, on Windows.

### Added

- **Claudy's card, for real.** The card is now drawn as Claudy's is: 20-pixel corners, glass with
  a light sheen and a coral halo from the top-left corner, a hairline lit from the same side, and
  its own soft shadow, computed from the card's outline. Windows' frame and small corners are gone.
- **Claudy's rounded type.** Claudy writes in SF Pro Rounded, which only Apple's systems may carry;
  Claudio now ships Nunito, the closest rounded face under an open licence, at Claudy's sizes and
  line heights. Figures and their "%" share one baseline, and every capsule (the tabs, the pills,
  the sign-in button) is a true capsule rather than an oval.
- **Claudy's glows:** the soft halo under the session's gauge, the avatar's and the sign-in
  button's coral glow, and the account card's shadow.
- **The figure next to the clock.** While Claudio lives in the notification area, the lead figure
  ("42%") sits in an icon of its own beside the mascot, as Claudy writes it next to its own, in
  the taskbar's ink.
- **The update bubble.** While Claudio lives in the notification area, a bubble rises above the
  clock once per version: the mascot waving, the version, "What's new", Later or Update. The
  icon carries Claudy's coral dot while an update waits.
- **The explosion next to the clock too.** When a quota fills with Claudio running, the icon's
  laptop explodes once before the dead state, as the card's does.
- **Windows' "Animation effects" setting** is honoured as Claudy honours "Reduce motion": the
  mascot holds still, in the card and next to the clock.
- **Claudy's window manners:** the card goes back to its corner when it changes size, finds a
  screen again when one is unplugged or the taskbar moves, and Ctrl+Q quits.
- A `configDir` setting points Claudio at Claude Code's folder, as Claudy's `claudy.configDir`.
- Screen readers name the card's controls; the mascot, a decoration, is skipped.
- **A sharp icon next to the clock.** At 16 and 24 pixels the mascot was averaged into a smudge; it
  is now redrawn at half size, one pixel per block of four, so it stays pixel art.
- **The whole card.** Under the session: the weekly and per-model columns, today's and the week's
  tokens, the 7-day curve (hover a day for its figures), the "Details" accordion with the week
  split by model and by project, the sessions of the day and the last update, with a refresh
  button.
- **The pace marker** on every gauge: the share of the window already elapsed. The session says
  "15 pts ahead of pace", the columns "+15"; red past 20 points ahead.
- **Minimal mode:** one strip with the mascot, the figure and the reset. A click on the card
  switches between the two; Claudio remembers which.
- **The account card:** a click on the avatar shows who is signed in, the plan, the organisation,
  and "Sign out".
- **Sign in to Claude** on a PC where Claude Code is not signed in, or not installed: the official
  claude.ai sign-in, in the browser, with the code to paste when the browser cannot hand it back.
  Claudio's own token lives in the Windows Credential Manager; Claude Code's is still only read.
  "Sign out" stops Claudio reading the quotas, across relaunches, and leaves Claude Code signed in.
- **Ports tab:** the TCP ports Claude Code left listening, orphaned servers included, and a click
  to close them. Only processes Claude launched are listed, read from their environment; a live
  Claude session and Claudio itself are never closed.
- **In the notification area:** the right-click menu can move Claudio there. A click on the icon
  then opens a short card above the clock: the quotas as rings with their pace, the week as bars,
  the split by model, and the account.
- **The right-click menu** of the card: Refresh, full or minimal mode, where Claudio shows itself,
  sign in or out, always on top, launch at sign-in, Quit.
- **Launch at sign-in**, from that menu, without admin rights.
- The mascot explodes when a quota is full and stays dead until it frees up, in the card and next
  to the clock; it waves while a new version is out, until the card is clicked.
- **Updates in the card:** a new version downloads in the background and the card offers to
  restart into it; it is applied when Claudio quits anyway. A beta follows the betas.
- Past 95 % of a quota, the card's hairline turns red.
- The status-line relay: when Anthropic cannot be reached, Claudio reads the counters Claude Code
  received itself, if its status line drops them in `%LOCALAPPDATA%\Claudio\usage-bridge.json`.
- The card reads the account again when the PC wakes from sleep.

### Fixed

- **Claudio crashed on launch.** The release did not carry its compiled interface
  (`Claudio.pri`), so the window could not load and Claudio closed before showing anything. The
  build now produces it, and CI fails if it is missing.
- **Claudio no longer takes the focus when it starts,** which could pull a full-screen game or
  presentation into the background. A click on the card leaves the keyboard where it was too, as
  Claudy's panel does; only pasting the sign-in code takes it.
- **The icons next to the clock answer again.** The transparent margin that carries the card's
  shadow reached over the taskbar and caught the clicks meant for the "^" button and the icons; it
  now stops at the taskbar, and only the card and its visible shadow take clicks.
- **The card stays on top.** It could fall behind other windows once Windows had rewritten its
  style.
- **A drag cannot run away.** When the mouse button was released outside the card, the card could
  keep following the pointer; it now stops as soon as the button is up.
- **One project per folder.** Claude Code records the same folder as `G:\…` and `g:\…`; the split
  by project showed it twice.

### Changed

- The README, its French version and the documentation follow Claudy's: what Claudio does, the
  gestures, how it works and how to build it. The mascot GIFs are drawn from Claudy's frames by
  `Scripts/make-mascot-gifs.py`.
- The app's code is laid out as Claudy's (`App/`, `Services/`, `ViewModels/`, `Theme/`, `Views/`),
  under the same names where Claudy has the same piece.

## 1.0.0-beta.1 (1 October 2026)

The first build to try on a real PC, ahead of 1.0.0.

### Added

- The logic reads Anthropic's quota answers exactly as Claudy does: 5-hour and weekly windows,
  the per-model window, and the monthly spend cap of usage-billed plans. Checked against every
  case Claudy's own tests run.
- Claudy's look, read from its exported design tokens, and its pixel mascot, drawn from the same
  frames.
- The card shows your real quotas: the 5-hour session, or the monthly spend on a plan billed on
  usage, with its reset and how long until it. The token is Claude Code's, read and never
  rewritten. The card reads the account every three minutes, keeps the last reading when
  Anthropic cannot be reached and says so, and shows a dash rather than an estimate.
- The mascot types while a session runs and turns amber at 75 %, red at 90 %.
- Claudio in the notification area, next to the clock: the mascot, typing while a session runs
  and in the gauge's colour, the session figure in its tooltip. A click shows or hides the card;
  a right click opens Refresh and Quit.
- Tokens used today and over seven days on this PC, read from Claude Code's transcripts, in
  Windows and in every running WSL distribution. Each response counts once, at its final size,
  whatever folder holds it: checked against the transcript cases Claudy runs.
- Claudy's icon, the pixel mascot at its desk, on the desktop, in the Start menu and the taskbar.
- An installer: Claudio's icon on the desktop and in the Start menu, found by Windows search,
  installed without admin rights and updated in place. One Claudio runs at a time.
