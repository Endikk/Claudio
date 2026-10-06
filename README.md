<div align="center">

# ✳︎ Claudio

**Your real Claude quotas, on your Windows desktop.**

[![Downloads](https://img.shields.io/github/downloads/Endikk/Claudio/total?label=downloads&color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/releases)
[![Stars](https://img.shields.io/github/stars/Endikk/Claudio?color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/stargazers)
[![Release](https://img.shields.io/github/v/release/Endikk/Claudio?include_prereleases&color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/releases)
[![Windows](https://img.shields.io/badge/Windows-11-black?style=flat-square)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-black?style=flat-square)](LICENSE)

🇫🇷 [This README in French](README.fr.md)

<p align="center"><img src="docs/claudio-typing.gif" width="132" alt="Claudio, the pixel mascot, typing at its laptop"></p>

<p align="center"><img src="docs/claudio-card.png" width="352" alt="The Claudio card: 5h session, weekly quotas, daily totals, 7-day curve, split by model and project"></p>

</div>

A floating Windows widget for your Claude usage, the sibling of [Claudy](https://github.com/Endikk/Claudy)
on macOS. Borderless, always on top, draggable, compact or full. The gauges show your account's
**real quotas** — the same figures as claude.ai ▸ Usage and `/usage` — while the token detail comes
from Claude Code's local transcripts, in Windows and in WSL.

- **Real numbers, or none.** Percentages come from Anthropic's API alone. When it says nothing,
  the gauges read "—" rather than an estimate. On a PC without Claude Code where nobody signed in,
  a sample set labelled "demo" shows what Claudio looks like.
- **Ports tab.** Lists the TCP ports Claude Code left listening, orphaned sessions included, and
  closes them on a click. Attribution reads the Claude markers a process inherits in its
  environment, so nothing else on your PC is ever listed.
- **Nothing leaves the PC.** No telemetry, no third-party server, no conversation read or sent.
  The only network requests go to Anthropic's API.

## Install

```powershell
scoop bucket add claudio https://github.com/Endikk/scoop-claudio
scoop install claudio
```

<details>
<summary>Other routes</summary>

**Installer:** download `Claudio-win-x64-Setup.exe` (or `win-arm64`) from the
[releases](https://github.com/Endikk/Claudio/releases) and run it. Claudio installs for you alone,
without admin rights, puts its icon on the desktop and in the Start menu, and updates itself from
then on.

Claudio is **not code-signed** yet, so Windows SmartScreen may warn on the first launch: *More
info* ▸ *Run anyway*. Prefer not to? Build it yourself below; the code is short and auditable.

**From source (Windows 11, .NET 10 SDK):**

```powershell
git clone https://github.com/Endikk/Claudio.git
cd Claudio
dotnet publish src/Claudio.App -c Release -p:Platform=x64 -r win-x64 -o publish
.\publish\Claudio.exe
```

winget is not available yet.

</details>

## Use

Claudio has no window of its own in the taskbar. It shows as a floating card above the clock; in
the notification area, the mascot and, beside it, the lead figure ("42%"), where a click opens a
short card; or as an island hanging from the top of the screen, the mascot and the figure on
either side, which opens on hover as Claudy's opens round the notch. Right-click any of them to
move it.

| Gesture | Effect |
|---|---|
| Drag the card | Move the widget |
| Click the session or the minimal strip | Switch between full and minimal mode |
| `usage` / `ports` | Switch between quotas and the ports Claude left open |
| Right-click | Refresh · Mode · Placement (card, notification area, top of the screen) · Sign in · Always on top · Launch at sign-in · Quit |
| Hover the island at the top of the screen | Open the session, its pace and reset, and the other quotas |
| Click the icon next to the clock | Show or hide the card, open the short card, or open the island |
| Click the avatar | Account card |
| Click "Details" | Split by model and top projects |
| Ctrl+R or F5 | Refresh |
| Ctrl+Q | Quit |

Refreshes every 3 minutes, and immediately when the PC wakes.

## Documentation

- [How it works](docs/how-it-works.md) — data sources, quota invariants, status-line bridge, pace
  marker, the island, demo mode, privacy.
- [Development](docs/development.md) — building, testing, project structure, the window
  constraints worth knowing before touching it.
- [Architecture](docs/architecture.md) — how Claudio stays in step with Claudy, file for file.

## Branches

| Branch | Role |
|---|---|
| `main` | Stable. What is released and what Scoop installs. |
| `develop` | The moving one. Every feature lands here first and lives here until it has been used for real; `main` only ever receives what has held up. |

Open pull requests against `develop`.

## Contributing

A bug, an idea, a figure that does not match claude.ai? Open an
[issue](https://github.com/Endikk/Claudio/issues) — a screenshot and
`%LOCALAPPDATA%\Claudio\api.log` are welcome. PRs are open.

MIT, maintained by [@Endikk](https://github.com/Endikk).

<p align="center"><img src="docs/claudio-overload.gif" width="196" alt="Claudio at 100 %: the laptop explodes and the mascot is left ashen, with crossed-out eyes"></p>
