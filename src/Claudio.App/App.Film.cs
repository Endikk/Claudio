#if DEBUG
using Claudio.Core.Models;
using Claudio.Core.Services;
using Claudio.Core.ViewModels;

namespace Claudio.App;

/// <summary>
/// <c>--film &lt;folder&gt;</c>, in Debug builds only: Claudio plays the README's recording by itself,
/// on made-up sessions, agents, ports and quotas, the way Claudy's recording was filmed from its own
/// views fed with sample data. Nothing real is read or touched: no session file, no port, no
/// notification, no shortcut. Each scene's name and the window it shows are written to
/// <c>scene.txt</c> in the folder, for <c>Scripts/record-demo.ps1</c> to film it, and Claudio
/// quits after the last one. <c>Scripts/make-demo-video.py</c> then lays the frames on the video.
/// </summary>
public partial class App
{
    /// <summary>The folder after <c>--film</c>, or null when Claudio is not filming.</summary>
    internal static string? FilmFolder { get; } = FilmArgument();

    internal static bool IsFilming => FilmFolder is not null;

    private static string? FilmArgument()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, "--film");
        return index >= 0 && index + 1 < arguments.Length && arguments[index + 1].Length > 0 ? Path.GetFullPath(arguments[index + 1]) : null;
    }

    private readonly FilmScanner _filmScanner = new();
    private double? _filmPercent;

    /// <summary>The made-up reading: the demo set's figures, an account's look, neutral project names.</summary>
    private UsageSnapshot FilmSnapshot(UsageSnapshot demo)
    {
        var week = demo.WeekTokens;
        var session = _filmPercent is { } percent ? demo.Session with { Percent = percent } : demo.Session with { Percent = 0.46 };
        return demo with
        {
            Session = session,
            // A third of the week gone, one window a little ahead of it and one a little behind.
            Weekly = Week(demo.Weekly, 0.41),
            Scoped = Week(demo.Scoped, 0.23) with { Title = "Sonnet" },
            QuotaSource = QuotaSource.Api,
            IsSignedIn = true,
            Account = new Account("Claudio", string.Empty, "Max", string.Empty, false),
            ActiveModel = "Opus 5.5",
            SessionCount = 3,
            Projects =
            [
                new ProjectUsage("storefront", "storefront", (long)(week * 0.44), 0.44),
                new ProjectUsage("api", "api", (long)(week * 0.27), 0.27),
                new ProjectUsage("docs-site", "docs-site", (long)(week * 0.18), 0.18),
                new ProjectUsage("scripts", "scripts", (long)(week * 0.11), 0.11),
            ],
        };
    }

    private static UsageWindow Week(UsageWindow window, double percent)
    {
        var start = DateTimeOffset.UtcNow.AddDays(-7 * 0.31);
        return window with { Percent = percent, WindowStart = start, ResetDate = start.AddDays(7), IsMeasured = true };
    }

    /// <summary>What the Ports tab shows: two live ports, an orphaned one, an MCP server left behind.</summary>
    private static void FilmPorts(PortsViewModel ports)
    {
        var now = DateTimeOffset.UtcNow;
        ListeningPort Port(int pid, int number, string command, string project, double minutes, PortAttribution attribution) =>
            new($"{pid}-{number}", pid, number, command, project, now.AddMinutes(-minutes), attribution, attribution == PortAttribution.Live ? 1 : null);
        ports.Film(new PortScanState.Ready(
        [
            Port(41001, 3000, "node", "storefront", 31, PortAttribution.Live),
            Port(41002, 5173, "vite", "docs-site", 192, PortAttribution.Orphan),
            Port(41003, 8000, "python3", "api", 5, PortAttribution.Live),
        ], IsDegraded: false),
        [new McpOrphan("mcp-41004", 41004, now.AddHours(-11), "chroma-mcp", null, 174UL << 20)]);
    }

    private void StartFilm(Microsoft.UI.Dispatching.DispatcherQueue ui)
    {
        Directory.CreateDirectory(FilmFolder!);
        var steps = new List<(double At, Action Act)>();
        double clock = 0;
        void At(double seconds, Action act) => steps.Add((clock + seconds, act));
        void Scene(string name, string window, double length, params (double At, Action Act)[] acts)
        {
            At(0, () => Mark(name, window));
            foreach (var (at, act) in acts)
            {
                At(at, act);
            }
            clock += length;
        }

        var model = _model!;
        // Set the stage, and give the first readings time to land.
        At(0, () =>
        {
            model.Place(Placement.Widget);
            model.Tab = Views.CardTab.Usage;
            if (!model.IsMinimal)
            {
                model.ToggleMode();
            }
            if (model.IsDetailsExpanded)
            {
                model.ToggleDetails();
            }
        });
        clock = 5;
        Scene("strip", "Claudio", 4);
        Scene("card", "Claudio", 5, (0, model.ToggleMode));
        Scene("details", "Claudio", 4.5, (0, model.ToggleDetails));
        Scene("sessions", "Claudio", 6,
              (0, () =>
              {
                  model.ToggleDetails();
                  model.Tab = Views.CardTab.Sessions;
              }),
              (2.5, () => _agents?.ToggleExpanded(_agents.Sessions.First(session => session.Name == "api"))),
              // The room opens now, unfilmed: an agent stays put twenty seconds after it sits.
              (3, () =>
              {
                  OpenRoom();
                  if (Room() is { } room && room.Projects.FirstOrDefault(project => project.Id.EndsWith("storefront", StringComparison.Ordinal)) is { } storefront)
                  {
                      room.SelectedId = storefront.Id;
                  }
              }));
        Scene("ports", "Claudio", 4.5, (0, () => model.Tab = Views.CardTab.Ports));
        Scene("island", "island", 7,
              (0, () =>
              {
                  model.Tab = Views.CardTab.Usage;
                  model.Place(Placement.Notch);
              }),
              (2.2, () => _notch?.ToggleFromShortcut()));
        Scene("room", "Claudio · Agents", 6.5,
              (0, () =>
              {
                  _notch?.ToggleFromShortcut();
                  model.Place(Placement.Widget);
              }));
        Scene("moves", "Claudio · Agents", 7, (0.3, () => _filmScanner.Phase = 1));
        Scene("desk", "Claudio · Agents", 5.5, (0.6, () =>
        {
            if (Room() is { } room)
            {
                room.SelectedAgentId = "a-tests";
            }
        }));
        Scene("overload", "Claudio", 7,
              (0, () =>
              {
                  if (Room() is { } room)
                  {
                      room.SelectedAgentId = null;
                  }
                  CloseRoom();
                  _filmPercent = 0.86;
                  if (!model.IsMinimal)
                  {
                      model.ToggleMode();
                  }
                  _ = model.RefreshAsync();
              }),
              (2.2, () =>
              {
                  _filmPercent = 1;
                  _ = model.RefreshAsync();
              }));
        At(0, () => Mark("done", string.Empty));
        At(1, Quit);

        var started = DateTimeOffset.UtcNow;
        var next = 0;
        steps.Sort((a, b) => a.At.CompareTo(b.At));
        var timer = ui.CreateTimer();
        timer.Interval = TimeSpan.FromMilliseconds(50);
        timer.IsRepeating = true;
        timer.Tick += (_, _) =>
        {
            var elapsed = (DateTimeOffset.UtcNow - started).TotalSeconds;
            while (next < steps.Count && steps[next].At <= elapsed)
            {
                try
                {
                    steps[next].Act();
                }
#pragma warning disable CA1031 // A step that fails is written down; the film goes on.
                catch (Exception error)
#pragma warning restore CA1031
                {
                    DiagnosticLog.Append($"film: {error}");
                }
                next++;
            }
            if (next >= steps.Count)
            {
                timer.Stop();
            }
        };
        timer.Start();
    }

    /// <summary>The scene under way and the window that shows it, for the recorder.</summary>
    private static void Mark(string scene, string window) =>
        File.WriteAllText(Path.Combine(FilmFolder!, "scene.txt"), $"{scene}\n{window}\n");

    /// <summary>Every action Claudio takes on a session, answered without touching anything.</summary>
    private sealed class FilmActions : ISessionActions
    {
        public bool IsSession(int pid, DateTimeOffset? startedAt) => true;

        public SignalResult Interrupt(int pid, DateTimeOffset startedAt) => SignalResult.Sent;

        public bool BringForward(HostApp host) => true;
    }

    /// <summary>
    /// Three sessions open in three folders: storefront waiting for an answer, with its team at
    /// work; api busy with two agents; docs-site at rest. <see cref="Phase"/> 1 turns two agents to
    /// other work, so that they walk across the room.
    /// </summary>
    private sealed class FilmScanner : IAgentScanning
    {
        private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;

        public int Phase { get; set; }

        public Task<ClaudeActivity> ScanActivityAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            var code = new HostApp("Visual Studio Code", Environment.ProcessId);
            var terminal = new HostApp("Terminal", Environment.ProcessId);
            ClaudeSession Session(string id, int pid, string folder, SessionState state, double openedMinutes, HostApp host, string prompt,
                                  string? waitingFor = null, double sinceSeconds = 0) => new()
                                  {
                                      Id = id,
                                      Pid = pid,
                                      ProcessStartedAt = _start.AddMinutes(-openedMinutes),
                                      Name = folder,
                                      Cwd = $@"C:\dev\{folder}",
                                      OpenedAt = _start.AddMinutes(-openedMinutes),
                                      Status = state,
                                      WaitingFor = waitingFor,
                                      StatusSince = now.AddSeconds(-sinceSeconds),
                                      Host = host,
                                      LastPrompt = prompt,
                                  };
            var storefront = Session("s-storefront", 51001, "storefront", SessionState.Waiting, 190, code,
                                     "Ship the cart page with tests and a benchmark", "permission prompt", 25 + (now - _start).TotalSeconds);
            var api = Session("s-api", 51002, "api", SessionState.Busy, 150, terminal, "Paginate the orders endpoint");
            var docs = Session("s-docs", 51003, "docs-site", SessionState.Idle, 333, code, "Fix the broken links", sinceSeconds: 1200);

            RunningAgent Agent(string id, ClaudeSession session, string title, double startedMinutes, (string Tool, string Text)[] calls,
                               bool finished = false, double quietSeconds = 2)
            {
                var actions = new List<AgentAction>();
                var edited = new List<string>();
                // One call every twelve seconds over the last ten minutes, the newest a moment ago.
                for (var index = 0; index < 50; index++)
                {
                    var (tool, text) = calls[index % calls.Length];
                    var date = now.AddSeconds(-quietSeconds - ((49 - index) * 12));
                    var isShell = tool is "Bash";
                    var reading = AgentWorks.Read(tool, isShell ? text : null, isShell ? null : text);
                    var file = isShell ? null : Path.GetFileName(text);
                    actions.Add(new AgentAction(date, AgentActionKinds.Of(tool), tool, file, reading.Work, reading.Detail));
                    if (AgentActionKinds.Of(tool) == AgentActionKind.Edit && file is not null && !edited.Contains(file))
                    {
                        edited.Add(file);
                    }
                }
                return new RunningAgent
                {
                    Id = id,
                    SessionId = session.Id,
                    SessionPid = session.Pid,
                    SessionStartedAt = session.ProcessStartedAt,
                    SessionName = session.Name,
                    Title = title,
                    StartedAt = _start.AddMinutes(-startedMinutes),
                    LastActivity = actions[^1].Date,
                    IsFinished = finished,
                    ActionCount = actions.Count + (int)startedMinutes,
                    Recent = actions,
                    LastAction = actions[^1],
                    EditedFiles = edited,
                };
            }

            (string, string)[] reading = [("Grep", "orders"), ("Read", @"C:\dev\storefront\src\cart.ts"), ("Read", @"C:\dev\storefront\src\pricing.ts")];
            (string, string)[] testing = [("Bash", "npm test -- cart"), ("Edit", @"C:\dev\storefront\test\cart.test.ts"), ("Bash", "npm test")];
            (string, string)[] building = [("Bash", "npm run build"), ("Bash", "tsc --noEmit")];
            (string, string)[] writingDocs = [("Edit", @"C:\dev\storefront\docs\cart.md"), ("Read", @"C:\dev\storefront\README.md")];
            var agents = new List<RunningAgent>
            {
                Agent("a-cart", storefront, "Write the cart page", 14, [("Edit", @"C:\dev\storefront\src\cart.tsx"), ("Edit", @"C:\dev\storefront\src\cart.css")]),
                Agent("a-pricing", storefront, "Spec the pricing rules", 12, [("Edit", @"C:\dev\storefront\specs\pricing\spec.md")]),
                Agent("a-tests", storefront, "Write the cart tests", 11, testing),
                Agent("a-bench", storefront, "Benchmark the cart render", 9, [("Bash", "hyperfine 'npm run render'"), ("Bash", "npm run bench")]),
                Agent("a-search", storefront, "Find every use of the old cart", 8, Phase == 0 ? reading : testing),
                Agent("a-build", storefront, "Check the build", 7, Phase == 0 ? building : writingDocs),
                Agent("a-review", storefront, "Review the cart diff", 16, [("Bash", "git diff main...HEAD"), ("Read", @"C:\dev\storefront\src\cart.tsx")], finished: true, quietSeconds: 90),
                Agent("a-orders", api, "Map the orders queries", 10, [("Grep", "orders"), ("Read", @"C:\dev\api\orders.py")]),
                Agent("a-pages", api, "Write pagination tests", 10, [("Edit", @"C:\dev\api\test_orders.py"), ("Bash", "pytest -q")]),
            };
            agents.Sort((first, second) => first.StartedAt.CompareTo(second.StartedAt));
            return Task.FromResult(new ClaudeActivity { Sessions = new[] { storefront, api, docs }, Agents = agents });
        }
    }
}
#endif
