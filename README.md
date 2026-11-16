<div align="center">

# ✳︎ Claudio

**Your real Claude quotas, on your Windows desktop.**

[![Downloads](https://img.shields.io/github/downloads/Endikk/Claudio/total?label=downloads&color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/releases)
[![Stars](https://img.shields.io/github/stars/Endikk/Claudio?color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/stargazers)
[![Release](https://img.shields.io/github/v/release/Endikk/Claudio?color=D97757&style=flat-square)](https://github.com/Endikk/Claudio/releases/latest)
[![Windows](https://img.shields.io/badge/Windows-11-black?style=flat-square)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-black?style=flat-square)](LICENSE)

🇫🇷 [This README in French](README.fr.md)

<p align="center"><img src="docs/claudio-typing.gif" width="132" alt="Claudio, the pixel mascot, typing at its laptop"></p>

<p align="center"><img src="docs/video-readme.gif" width="620" alt="Claudio in a minute: the compact strip, the usage card and its details, the Sessions and Ports tabs, the island at the top of the screen, the agent room where each agent sits at a desk, and the mascot at 100 %"></p>

</div>

A floating Windows widget for your Claude usage. Borderless, always on top, draggable, compact or
full. The gauges show your account's **real quotas** — the same figures as claude.ai ▸ Usage and
`/usage` — while the token detail comes from Claude Code's local transcripts, in Windows and in WSL.

- **Real numbers, or none.** Percentages come from Anthropic's API alone. When it says nothing,
  the gauges read "—" rather than an estimate.
- **Sessions tab.** Every Claude Code session open on the PC, the ones waiting for your answer
  first, each with the agents it runs. "Open" brings forward the app it runs in; "Interrupt"
  sends it Ctrl+C.
- **Agent room.** A window of its own: one open space per project, every agent at a desk in
  the area of its work, its screen showing what it does (code, tests, build, benchmark, infra…),
  walking to another desk when its work changes. The city in its window lights up and goes to
  sleep with the hour.
- **Notifications.** Session at 80 %, limit coming before the reset, session back, a session
  waiting for you, an agent gone silent or done. One switch each.
- **Ports tab.** Lists the TCP ports Claude Code left listening, orphaned sessions included, and
  the MCP servers a closed session left running, and kills them on a click. Attribution reads the
  Claude markers a process inherits in its environment, so nothing else on your PC is ever
  listed.
- **Nothing leaves the PC.** No telemetry, no third-party server, no conversation sent. The only
  text Claudio reads from your conversations is the last prompt of each open session, shown in
  the Sessions tab. The only network requests go to Anthropic's API.

## Install

```powershell
scoop bucket add claudio https://github.com/Endikk/scoop-claudio
scoop install claudio
```

<details>
<summary>Other routes</summary>

**Installer:** download `Claudio-win-x64-Setup.exe` (or `win-arm64`) from the
[latest release](https://github.com/Endikk/Claudio/releases/latest) and run it. Claudio installs
for you alone, without admin rights, puts its icon on the desktop and in the Start menu, and
updates itself from then on.

Claudio is **not code-signed** — free distribution, no code-signing certificate — so Windows
SmartScreen may warn on the first launch: *More info* ▸ *Run anyway*. Prefer not to? Build it
yourself below; the code is short and auditable.

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

The app has no window in the taskbar. It shows as a floating card, as an icon in the notification
area next to the clock, or as an island at the top of the screen. Right-click any of them to move
it.

| Gesture | Effect |
|---|---|
| Drag the card | Move the widget |
| Click the minimal strip | Switch to full mode |
| `usage` / `ports` / `sessions` | Switch between quotas, what Claude left running, and the open sessions |
| Right-click | Refresh · Mode · Placement (card, notification area, top of the screen) · Notifications · Show or hide · Export usage as CSV · Sign in · Always on top · Launch at sign-in · Quit |
| Ctrl+Alt+C, from any app | Show or hide Claudio |
| Ctrl+Alt+R, from any app | Open or close the agent room |
| Hover the island | Open the session, its pace and reset, and the other quotas under the island |
| Click the avatar | Account card |
| Click "Details" | Split by model and top projects |

Refreshes every 3 minutes, and immediately when the PC wakes.

## Documentation

- [How it works](docs/how-it-works.md) — data sources, quota invariants, status-line bridge, pace
  marker, demo mode, privacy.
- [Development](docs/development.md) — running, building the installer, project structure, the
  window constraints worth knowing before touching it.

## Star history

<a href="https://www.star-history.com/?repos=Endikk%2FClaudio&type=date&releases=&legend=bottom-right">
 <picture>
   <source media="(prefers-color-scheme: dark)" srcset="https://api.star-history.com/chart?repos=Endikk/Claudio&type=date&theme=dark&logscale&legend=bottom-right" />
   <source media="(prefers-color-scheme: light)" srcset="https://api.star-history.com/chart?repos=Endikk/Claudio&type=date&logscale&legend=bottom-right" />
   <img alt="Star History Chart" src="https://api.star-history.com/chart?repos=Endikk/Claudio&type=date&logscale&legend=bottom-right" />
 </picture>
</a>

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

<p align="center"><img src="docs/claudio-overload.gif" width="212" alt="Claudio at 100 %: the laptop explodes and the mascot is left ashen, with crossed-out eyes"></p>
