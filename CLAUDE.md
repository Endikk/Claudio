# Claudio

Claudio is the Windows app (C#, .NET 10, WinUI 3) of a pair whose macOS app is Claudy
(github.com/Endikk/Claudy, Swift). Claudy is the reference for the look and for how Anthropic's
answers are read; the two apps share data, never code.

## Rules

- `claudy/` is pulled from Claudy by `Scripts/sync-claudy.ps1` (only `Design/` and `Fixtures/`).
  Never edit it here, and never download Claudy's Swift sources into this repository.
- Port a Claudy change file for file: types keep the Swift names (see docs/architecture.md).
- One fixed reading, one fixture: behaviour changes land in Claudy's `Fixtures/` first.
- `Claudio.Core` stays free of Windows APIs so its tests run on Linux:
  `dotnet test tests/Claudio.Core.Tests`.
- Numbers come from Anthropic's API or are not shown. Never estimate a quota.
- Conventional Commits; pull requests go to `develop`; `main` is what is released.
