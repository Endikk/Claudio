# Architecture

[← README](../README.md)

## Layout

```
src/Claudio.Core/    the logic, no UI, no Windows API: runs and is tested anywhere
  Models/            quota readings (QuotaModels.cs)
  Services/          reading Anthropic's answers, plan labels, model names
  Design/            Claudy's tokens and mascot, read from the embedded shared data
src/Claudio.App/     the WinUI 3 app (Windows App SDK, unpackaged, self-contained)
tests/               xUnit; includes the fixtures shared with Claudy
claudy/              Design/ and Fixtures/ pulled from Claudy, never edited here
Scripts/             sync-claudy.ps1
```

## Claudy is the reference

Claudy (macOS, Swift) and Claudio (Windows, C#) are two native apps that share no code. What they
share is data, which Claudy exports and Claudio pulls into `claudy/`:

- **`Design/`**: colours, metrics, shadow and springs in the W3C Design Tokens format, and the
  pixel mascot as frames of characters. Claudio embeds these files and reads them at run time, so
  its look cannot drift from Claudy's.
- **`Fixtures/`**: real answers from Anthropic's API and the reading both apps must take from
  them. Claudy's tests and Claudio's run the very same files.

`Scripts/sync-claudy.ps1` fetches those two folders, and only them, with a sparse partial clone:
no Swift source is downloaded. Every Monday the *Sync with Claudy* workflow does it against
Claudy's latest release and opens a pull request. When Claudy changes how it reads an answer, the
fix lands there with a new fixture; the sync pull request then fails here until the same change
is ported, file for file.

## Porting from Claudy

Claudio's types keep the Swift names, so a change to `ClaudeAccountClient.swift` lands in the C#
file of the same role:

| Claudy (Swift) | Claudio (C#) |
|---|---|
| `Models/QuotaModels.swift` | `Claudio.Core/Models/QuotaModels.cs` |
| `ClaudeAccountClient.parseUsage` | `Claudio.Core/Services/UsageParser.cs` |
| `AccountLoader.planLabel` | `Claudio.Core/Services/PlanLabel.cs` |
| `ModelName.swift` | `Claudio.Core/Services/ModelName.cs` |
| `Theme.swift`, `ClaudyTyping`, `ClaudyOverload`, `ClaudyWave` | `Claudio.Core/Design/` (from the shared data) |

## Installing and updating

Claudio ships with [Velopack](https://velopack.io), the installer and updater most .NET desktop apps
use today. `Setup.exe` installs into `%LocalAppData%\Claudio` for the current user, without admin
rights, and puts a shortcut on the desktop and in the Start menu, which is what makes Claudio show
up in Windows search. `Program.Main` hands over to Velopack first, so installing, updating and
uninstalling (Settings ▸ Apps) all go through it.

Installed copies look for a newer release on GitHub at launch, download it in the background and
apply it when Claudio quits (`Updates.cs`). x64 and ARM64 are separate channels, `win-x64` and
`win-arm64`, so each machine only ever receives its own build.

Every push builds both installers in CI (*Claudio-Setup-win-x64* artifact). A `vX.Y.Z` tag builds
them again, attests their provenance (`gh attestation verify`), and publishes the release with
Velopack's update packages. The icon is Claudy's: `Scripts/make-icon.py` packs
`claudy/Design/icon/*.png` into `Assets/Claudio.ico`.
