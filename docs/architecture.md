# Architecture

[← README](../README.md)

## Layout

```
src/Claudio.Core/    the logic, no UI, no Windows API: runs and is tested anywhere
  Models/            quota readings (QuotaModels.cs)
  Services/          reading Anthropic's answers, plan labels, model names
  Design/            Claudy's tokens and mascot, read from the embedded shared data
src/Claudio.App/     the WinUI 3 app (Windows App SDK, unpackaged, self-contained), laid out as Claudy:
  App/ Services/ ViewModels/ Theme/ Views/ (see development.md)
tests/               xUnit; includes the fixtures shared with Claudy
claudy/              Design/ and Fixtures/ pulled from Claudy, never edited here
Scripts/             sync-claudy.ps1, make-icon.py, make-mascot-gifs.py
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
| `Models/UsageModels.swift` | `Claudio.Core/Models/UsageModels.cs` |
| `Models/Placement.swift` | `Claudio.Core/Models/Placement.cs` (the card or the notification area; Windows has no notch) |
| `Models/PortModels.swift` | `Claudio.Core/Models/PortModels.cs` |
| `ClaudeAccountClient.parseUsage` | `Claudio.Core/Services/UsageParser.cs` |
| `AccountLoader.swift` | `Claudio.Core/Services/AccountLoader.cs`, its plan labels in `PlanLabel.cs` |
| `ModelName.swift` | `Claudio.Core/Services/ModelName.cs` |
| `ClaudeHome.swift` | `Claudio.Core/Services/ClaudeHome.cs` |
| `ClaudeCodeCredentials.swift` | `Claudio.Core/Services/ClaudeCodeCredentials.cs` (a file on Windows, not the keychain) |
| `ClaudeCredentials.swift` | `Claudio.Core/Services/ClaudeCredentials.cs` (+ the store itself, the Credential Manager rather than the keychain, in `Claudio.App/Services/ClaudeCredentialsStore.cs`) |
| `ClaudeOAuth.swift` | `Claudio.Core/Services/ClaudeOAuth.cs` |
| `ClaudeAccountClient.swift` | `Claudio.Core/Services/ClaudeAccountClient.cs` |
| `TranscriptScanner.swift`, `ProjectResolver.swift` | `Claudio.Core/Services/`, same names (+ WSL sources in `Claudio.App/Services/WslSources.cs`) |
| `UsageAggregator.swift`, `UsageBridge.swift` | `Claudio.Core/Services/`, same names |
| `UsageDataSource.swift` (`LocalUsageDataSource`) | `Claudio.Core/Services/UsageDataSource.cs`; no demo set, Claudio shows nothing it did not measure |
| `PortScanner.swift`, `PortReaper.swift`, `ProcessTable.swift`, `ProcessEnvironment.swift` | `Claudio.Core/Services/`, same names; what Claudy asks `lsof`, `ps` and `sysctl` comes from Win32 in `Claudio.App/Services/WindowsProcesses.cs` |
| `UsageViewModel.swift` | `Claudio.App/ViewModels/UsageViewModel.cs`, its static formatting in `Claudio.Core/Presentation/UsageFormat.cs` |
| `PortsViewModel.swift` | `Claudio.App/ViewModels/PortsViewModel.cs` (its texts in `Claudio.Core/Presentation/PortsText.cs`) |
| `UpdateChecker.swift` | `Claudio.App/Services/UpdateChecker.cs` (Velopack instead of Homebrew) |
| `LaunchAtLogin.swift` | `Claudio.App/Services/LaunchAtLogin.cs` (the user's `Run` key) |
| `Theme.swift`, `ClaudyTyping`, `ClaudyOverload`, `ClaudyWave`, `CardShadow` | `Claudio.Core/Design/` (from the shared data) |
| `RootView.swift`, `FloatingPanel.swift` | `Claudio.App/Views/RootView.cs` on `App/FloatingPanel.cs` |
| `MenuBarView.swift`, `MenuBarController.swift` | `Claudio.App/Views/MenuBarView.cs` in `App/MenuBarPopover.cs`, the icon in `App/MenuBarController.cs` |
| `Views/*.swift`, `Views/Components/*.swift` (`CardShadow` as `OutlineShadow`) | `Claudio.App/Views/`, same names |

The views are built in code rather than XAML, one class per SwiftUI view, so a Claudy view and its
port read side by side. The card is drawn as Claudy draws it: a borderless, transparent window
(`Theme/TransparentBackdrop.cs`) holding the rounded glass, its sheen and coral halo, the hairline, and
the drop shadow computed from the card's outline (`CardShadow.cs`) in a margin around it.

## Installing and updating

Claudio ships with [Velopack](https://velopack.io), the installer and updater most .NET desktop apps
use today. `Setup.exe` installs into `%LocalAppData%\Claudio` for the current user, without admin
rights, and puts a shortcut on the desktop and in the Start menu, which is what makes Claudio show
up in Windows search. `Program.Main` hands over to Velopack first, so installing, updating and
uninstalling (Settings ▸ Apps) all go through it.

Installed copies look for a newer release on GitHub at launch, download it in the background and
apply it when Claudio quits, or at once from the card (`Services/UpdateChecker.cs`). x64 and ARM64 are separate channels, `win-x64` and
`win-arm64`, so each machine only ever receives its own build.

Every push builds both installers in CI (*Claudio-Setup-win-x64* artifact). Releasing is changing
`<Version>` in `Directory.Build.props` and merging it into `main`: the *Release* workflow tags that
version (a suffix such as `-beta.1` makes it a pre-release), builds both installers again, attests their provenance (`gh attestation verify`), and publishes the release with Velopack's update
packages. A version already released is never built twice. The icon is Claudy's: `Scripts/make-icon.py` packs
`claudy/Design/icon/*.png` into `Assets/Claudio.ico`.
