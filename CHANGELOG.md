# Changelog

All notable changes to this project are documented here. Dates are release dates.

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
