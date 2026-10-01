<div align="center">

# ✳︎ Claudio

**Your real Claude quotas, on your Windows desktop.**

[![CI](https://github.com/Endikk/Claudio/actions/workflows/ci.yml/badge.svg?branch=develop)](https://github.com/Endikk/Claudio/actions/workflows/ci.yml)
[![Windows](https://img.shields.io/badge/Windows-11-black?style=flat-square)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-black?style=flat-square)](LICENSE)

</div>

> **In development.** There is no release to install yet.

Claudio is the Windows sibling of [Claudy](https://github.com/Endikk/Claudy): the same pixel
mascot, the same look and the same rule, **real numbers or none**. The gauges show your account's
own quotas, as claude.ai ▸ Usage reports them, and never an estimate.

## What it will do

- A floating card in the corner above the clock, and an icon in the notification area.
- The 5-hour session, the weekly quotas, and the monthly spend cap of Enterprise plans.
- The pace marker: ahead of the clock or behind it.
- Claude Code's usage from Windows **and from WSL**, read on this machine.
- Nothing leaves the machine but the requests to Anthropic's API.

## Install

Not yet. Releases will come through:

```powershell
winget install Endikk.Claudio
```

or [Scoop](https://scoop.sh):

```powershell
scoop bucket add claudio https://github.com/Endikk/scoop-claudio
scoop install claudio
```

## Build

Requires Windows 11, the [.NET 10 SDK](https://dotnet.microsoft.com/download) and Visual Studio
with the *WinUI application development* workload.

```powershell
dotnet test tests/Claudio.Core.Tests        # the logic, also runs on Linux and macOS
dotnet build src/Claudio.App -p:Platform=x64
```

See [docs/architecture.md](docs/architecture.md) for how the code is laid out and how it stays in
step with Claudy.

## Branches

| Branch | Role |
|---|---|
| `main` | Stable. What is released. |
| `develop` | The moving one. Open pull requests against it. |

MIT, maintained by [@Endikk](https://github.com/Endikk).
