# Changelog

All notable changes to this project are documented here. Dates are release dates.

## 1.0.0 (unreleased)

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
- Claudy's icon, the pixel mascot at its desk, on the desktop, in the Start menu and the taskbar.
- An installer: Claudio's icon on the desktop and in the Start menu, found by Windows search,
  installed without admin rights and updated in place. One Claudio runs at a time.
