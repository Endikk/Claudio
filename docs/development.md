# Developing Claudio

[← README](../README.md)

## Run (development)

Requires **Windows 11** and the [.NET 10 SDK](https://dotnet.microsoft.com/download). The Windows
App SDK and its XAML compiler come from NuGet, so Visual Studio is optional.

```powershell
dotnet build src/Claudio.App -p:Platform=x64
.\src\Claudio.App\bin\x64\Debug\net10.0-windows10.0.22621.0\win-x64\Claudio.exe
```

Use `-p:Platform=ARM64` on an ARM PC. A build run this way is not installed, so it never looks for
updates. A Debug build pretends a version is out with `--simulate-update 1.0.1`, as Claudy's
`-ClaudySimulateUpdate`: the coral dot, the wave, the update line and the bubble show, and the
bubble is never remembered.

## Build what is released

```powershell
dotnet publish src/Claudio.App/Claudio.App.csproj -c Release -p:Platform=x64 -r win-x64 -o publish
```

The published folder must hold `Claudio.pri`, the compiled XAML: without it `App.xaml` cannot load
and Claudio closes at launch before showing anything. `EnableMsixTooling` produces it for an
unpackaged app, and CI fails when it is missing.

Releasing is changing `<Version>` in `Directory.Build.props` and merging it into `main`; see
[architecture](architecture.md#installing-and-updating).

## Tests

```powershell
dotnet test tests/Claudio.Core.Tests
dotnet format src/Claudio.Core/Claudio.Core.csproj --verify-no-changes
dotnet format tests/Claudio.Core.Tests/Claudio.Core.Tests.csproj --verify-no-changes
```

The tests cover `Claudio.Core`, which holds every rule and no Windows API, so they also run on
Linux and macOS. They include the cases Claudy's own tests run (`claudy/Fixtures/`), the card's
shadow, the mascot's frames, the ports' attribution and guardrails, and Claudio's own sign-in. The
loopback sign-in tests play the browser themselves and skip when port 54545 is busy. No test reads
or writes the real Credential Manager or `api.log`.

## Structure

```
src/Claudio.Core/      every rule, no Windows API (tested on any OS)
├── Models/            UsageSnapshot and its parts · QuotaModels · AccountModels · Placement · PortModels
├── Services/          ClaudeHome · TranscriptScanner · ProjectResolver · UsageAggregator ·
│                      ClaudeAccountClient · ClaudeCodeCredentials · ClaudeCredentials · ClaudeOAuth ·
│                      UsageBridge · UsageDataSource · AccountLoader · ModelName · PlanLabel ·
│                      PortScanner · PortReaper · ProcessTable · ProcessEnvironment
├── Presentation/      UsageFormat (figures, dates, pace) · PortsText
└── Design/            Claudy's tokens, Theme, the mascot, CardShadow, the tray icon
src/Claudio.App/       the WinUI 3 app (Windows App SDK, unpackaged, self-contained)
├── App/               Program (Velopack first) · FloatingPanel (the glass window) · MenuBarController
│                      (the icon next to the clock) · MenuBarPopover (the short card above it)
├── Services/          UpdateChecker (Velopack) · LaunchAtLogin · Preferences · WindowsProcesses ·
│                      WslSources · ClaudeCredentialsStore (Credential Manager) · DiagnosticLog
├── ViewModels/        UsageViewModel · PortsViewModel
├── Theme/             Ui (type, inks, capsules) · Motion (springs) · TransparentBackdrop
├── Views/             RootView · FullView · MinimalView · OnboardingView · MenuBarView · PortsView ·
│                      Components/
└── Assets/            the icon, and Nunito, the rounded face (SIL Open Font License)
tests/                 xUnit, on Claudio.Core
claudy/                Design/ and Fixtures/ pulled from Claudy, never edited here
Scripts/               sync-claudy.ps1 · make-icon.py · make-mascot-gifs.py
```

Each Claudy view has its class here, under the same name, built in code rather than XAML so the
SwiftUI view and its port read side by side.

### Performance

`TranscriptScanner` keeps an offset per file and re-reads only the appended tail, after skipping
files untouched within the window and lines that contain no `"usage"`. The first pass over a large
history does not hold the card up: the account is read alongside it, and the sign-in card, which
shows no token count, does not wait for it. The API is polled every 3 minutes with a 60-second
cache, and the profile is re-read only every 6 hours.

### Window

The points worth knowing before changing it:

- **The card draws its own window.** `TransparentBackdrop` makes the window fully transparent —
  DWM honours a window's alpha only once its frame reaches into the client area and blur-behind is
  on over an empty region — and `FloatingPanel` draws Claudy's glass, sheen, coral halo, hairline
  and shadow itself. The thin dialog frame Windows leaves on a borderless window, and its rounded
  corners, are removed.
- **The shadow lives in a transparent margin** (`shadowInset`, 28 points) around the card, so any
  positioning reasons about the visible card, not the window. The window's region keeps clicks to
  the card and the part of the shadow that shows, and never covers the taskbar: the margin would
  otherwise catch the clicks meant for the icons next to the clock.
- **Anchored bottom-right.** The card's bottom-right corner is its anchor: it grows up and to the
  left when it changes size (mode, details, tab), and is clamped to the work area. A drag moves the
  anchor; a relaunch puts it back above the clock.
- **It never takes the focus** (`WS_EX_NOACTIVATE`), like Claudy's non-activating panel: starting
  at sign-in or clicking the card leaves the keyboard where it was, a full-screen game included.
  Only the pasted sign-in code needs the keyboard, and only then does the card take it.
- **Always on top is set on the window itself** (`SetWindowPos`), not through the presenter, which
  loses track of it once the extended style has been rewritten.
- **Fonts.** Claudy's face is SF Pro Rounded, which only Apple's systems may carry. Claudio ships
  Nunito, the closest rounded sans under an open licence, with SF's tight line height.
