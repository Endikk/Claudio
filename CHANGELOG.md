# Changelog

All notable changes to this project are documented here. Dates are release dates.

## Unreleased

### Added

- The logic reads Anthropic's quota answers exactly as Claudy does: 5-hour and weekly windows,
  the per-model window, and the monthly spend cap of usage-billed plans. Checked against every
  case Claudy's own tests run.
- Claudy's look, read from its exported design tokens, and its pixel mascot, drawn from the same
  frames.
- A first floating card with the typing mascot.
- An installer: Claudio's icon on the desktop and in the Start menu, found by Windows search,
  installed without admin rights and updated in place. One Claudio runs at a time.
