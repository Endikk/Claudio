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
bubble is never remembered. `--simulate-notch none` runs Claudio as if no screen could hold the
island, and `--simulate-notch 185x32` gives it a notch of that size, as Claudy's
`-ClaudySimulateNotch`.

`--room-preview` (Debug builds) opens the agent room alone, in a plain window, on a made-up scene:
a few agents in each area, one walking in by the door, one on the sofa, Claudy at work. It opens
beside a Claudio already running. `--room-preview 21.5` lights the room for that hour,
`--room-weather snow` (clear, overcast, rain, snow) chooses the weather, `--room-quota 0.95` sets
the session quota, and `--room-snapshot room.png` writes a frame drawn off screen to a file.

### The README's recording

`--film <folder>` (Debug builds) has Claudio play the README's recording by itself, on made-up
sessions, agents, ports and quotas: the strip, the card and its details, the Sessions and Ports
tabs, the island, the agent room, and the mascot at 100 %. Nothing real is read or touched, and it
takes nobody's keyboard. `Scripts/record-demo.ps1` runs it in a settings folder of its own and
films its windows with `PrintWindow`, whatever lies over them; `Scripts/make-demo-video.py` lays
the frames on the wallpaper with the captions and writes `docs/claudio-demo.gif` and
`docs/claudio-demo.fr.gif`, as Claudy's recording, in Claudio's own colours.

```powershell
dotnet build src/Claudio.App -p:Platform=x64
./Scripts/record-demo.ps1 -Out film
python Scripts/make-demo-video.py film
```

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
│                      · the agent room: RoomPlan (seats, routes) · RoomScene · RoomSky (hour, weather) · RoomVacuum
├── Services/          ClaudeHome · TranscriptScanner · ProjectResolver · UsageAggregator ·
│                      ClaudeAccountClient · ClaudeCodeCredentials · ClaudeCredentials · ClaudeOAuth ·
│                      UsageBridge · UsageDataSource · DemoUsageDataSource · AccountLoader ·
│                      ModelName · PlanLabel · NotchGeometry · NotchLayout · NotchHover ·
│                      PortScanner · PortReaper · ProcessTable · ProcessEnvironment ·
│                      McpScanner · AlertRules · UsageExport · AgentScanner · ProcessIdentity
├── ViewModels/        AgentsViewModel (the sessions and their agents, driven by the App) · RoomViewModel
├── Presentation/      UsageFormat (figures, dates, pace) · NotchActivity · PortsText · RoomText
└── Design/            Claudy's tokens, Theme, the mascot, CardShadow, NotchShape, the tray icon,
                       the room's sprite and screen symbols (RoomSprites)
src/Claudio.App/       the WinUI 3 app (Windows App SDK, unpackaged, self-contained)
├── App/               Program (Velopack first) · FloatingPanel (the glass window) · MenuBarController
│                      (the icon next to the clock) · MenuBarPopover (the short card above it) ·
│                      NotchController · NotchPanel (the island) · ClaudyMenu · UpdateBubblePanel ·
│                      PanelChrome (the windows' Win32 flags) · RoomWindow (the agent room)
├── Services/          UpdateChecker (Velopack) · LaunchAtLogin · Preferences · WindowsProcesses ·
│                      WindowsSessions · Notifier · WslSources · ClaudeCredentialsStore (Credential
│                      Manager) · DiagnosticLog
├── ViewModels/        UsageViewModel · PortsViewModel
├── Theme/             Ui (type, inks, capsules) · Motion (springs) · TransparentBackdrop
├── Views/             RootView · FullView · MinimalView · OnboardingView · MenuBarView · PortsView ·
│                      NotchView · NotchActivityView · Components/ · Room/ (RoomView · RoomOverview ·
│                      RoomSummary · RoomAgentCard, and RoomPainter · RoomCanvas on Win2D)
└── Assets/            the icon, and Nunito, the rounded face (SIL Open Font License)
tests/                 xUnit, on Claudio.Core
claudy/                Design/ and Fixtures/ pulled from Claudy, never edited here
Scripts/               sync-claudy.ps1 · make-icon.py · make-mascot-gifs.py · record-demo.ps1 ·
                       make-demo-video.py
```

Each Claudy view has its class here, under the same name, built in code rather than XAML so the
SwiftUI view and its port read side by side.

### Performance

`TranscriptScanner` keeps an offset per file and re-reads only the appended tail, after skipping
files untouched within the window and lines that contain no `"usage"`. The first pass over a large
history does not hold the card up: the account is read alongside it, and the sign-in card, which
shows no token count, does not wait for it. The API is polled every 3 minutes with a 60-second
cache, and the profile is re-read only every 6 hours.

### The agent room

The room is drawn with Win2D (`Microsoft.Graphics.Win2D`), call for call as Claudy draws it with
SwiftUI's `Canvas`: `RoomPainter` takes the scene's units (480 by 270, two pixels each at 100 %)
to a `CanvasDrawingSession`. What never moves (floor, walls, furniture, a desk's own furniture) is
drawn once into bitmaps, the screens' symbols and the signs' words into command lists; the rest
is drawn thirty times a second, the window's sky and city five times a second into a bitmap of
their own, what falls in front of them at thirty. Nothing is drawn while the window is hidden or
minimised. Claudy's SF Symbols are drawn with their Segoe Fluent Icons counterparts
(`Views/Room/RoomSprites.cs`). The night multiplies the room by a colour and screens the lamps
over it: SwiftUI's blend modes, which Win2D does with effects (`TintEffect`, `BlendEffect`) on a
bitmap of the room.

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
- **Every window is made at launch.** A WinUI window first made after another one had been shown
  appeared on screen without ever drawing its content: the island's is made with the others, and
  only shown when it is wanted.
- **The island draws itself frame by frame.** `NotchView` lays out the shape, the shadow (drawn once,
  then stretched by a nine-grid), the content's fade and the mascot's and figure's flight from one
  spring per frame; the window grows at once to the union of the frames and shrinks once the closing
  spring has settled, so the moving shape is never cut.
- **Fonts.** Claudy's face is SF Pro Rounded, which only Apple's systems may carry. Claudio ships
  Nunito, the closest rounded sans under an open licence, with SF's tight line height.
