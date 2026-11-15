using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// The agents list read from a fake Claude Code home: <c>sessions/&lt;pid&gt;.json</c> for the open
/// sessions, <c>projects/&lt;folder&gt;/&lt;session&gt;/subagents/agent-&lt;id&gt;.{meta.json,jsonl}</c> for
/// what they launched.
/// </summary>
public sealed class AgentScannerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"claudio-agents-{Guid.NewGuid():N}");
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_793_468_000);
    private const int LivePid = 4242;
    private const int ClosedPid = 4343;

    private static readonly JsonObject Running = new()
    {
        ["agentType"] = "general-purpose",
        ["description"] = "Make prescription file reading reliable",
    };

    public AgentScannerTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    // MARK: - Fixture

    private AgentScanner Scanner(HostApp? host = null, Func<int, DateTimeOffset?, bool>? isLive = null) => new(
        sessionsDirectory: () => Path.Combine(_root, "sessions"),
        projectsDirectories: () => [Path.Combine(_root, "projects")],
        isLive: isLive ?? ((pid, _) => pid == LivePid),
        hostApp: _ => host,
        now: () => Now);

    private void WriteSession(int pid, string id, string? name, string cwd, string? procStart = null, string status = "busy",
                              JsonObject? extra = null, string? file = null)
    {
        var session = new JsonObject { ["pid"] = pid, ["sessionId"] = id, ["cwd"] = cwd, ["status"] = status };
        if (name is not null)
        {
            session["name"] = name;
        }
        if (procStart is not null)
        {
            session["procStart"] = procStart;
        }
        foreach (var (key, value) in extra ?? [])
        {
            session[key] = value?.DeepClone();
        }
        var folder = Path.Combine(_root, "sessions");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, file ?? $"{pid}.json"), session.ToJsonString());
    }

    private string Subagents(string session)
    {
        var folder = Path.Combine(_root, "projects", "C--Users-me-Claire", session, "subagents");
        Directory.CreateDirectory(folder);
        return folder;
    }

    private string WriteAgent(string session, string id, JsonObject meta, JsonObject[] lines, DateTimeOffset modified)
    {
        var folder = Subagents(session);
        File.WriteAllText(Path.Combine(folder, $"agent-{id}.meta.json"), meta.ToJsonString());
        var transcript = Path.Combine(folder, $"agent-{id}.jsonl");
        File.WriteAllText(transcript, Jsonl(lines));
        File.SetLastWriteTimeUtc(transcript, modified.UtcDateTime);
        return transcript;
    }

    private static string Jsonl(params JsonObject[] lines)
    {
        var text = new StringBuilder();
        foreach (var line in lines)
        {
            text.Append(line.ToJsonString()).Append('\n');
        }
        return text.ToString();
    }

    private static string Stamp(DateTimeOffset date) =>
        date.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    private static JsonObject Prompt(DateTimeOffset date) => new()
    {
        ["type"] = "user",
        ["timestamp"] = Stamp(date),
        ["message"] = new JsonObject { ["role"] = "user", ["content"] = "Do the thing" },
    };

    private static JsonObject Tool(string id, string name, JsonObject input, DateTimeOffset date) => new()
    {
        ["type"] = "assistant",
        ["timestamp"] = Stamp(date),
        ["message"] = new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "tool_use", ["id"] = id, ["name"] = name, ["input"] = input }),
        },
    };

    /// <summary>The session's own transcript, beside its agents' folder.</summary>
    private string WriteTranscript(string session, params JsonObject[] lines)
    {
        var folder = Path.Combine(_root, "projects", "C--Users-me-Claire");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{session}.jsonl");
        File.WriteAllText(path, Jsonl(lines));
        return path;
    }

    private static JsonObject Typed(JsonNode content, bool meta = false)
    {
        var line = new JsonObject { ["type"] = "user", ["message"] = new JsonObject { ["role"] = "user", ["content"] = content } };
        if (meta)
        {
            line["isMeta"] = true;
        }
        return line;
    }

    private static JsonArray Blocks(params (string Type, string Key, string Value)[] blocks) =>
        [.. blocks.Select(block => (JsonNode)new JsonObject { ["type"] = block.Type, [block.Key] = block.Value })];

    private static JsonObject With(params (string Key, JsonNode Value)[] fields)
    {
        var json = new JsonObject();
        foreach (var (key, value) in fields)
        {
            json[key] = value;
        }
        return json;
    }

    private static byte[] Bytes(JsonObject line) => Encoding.UTF8.GetBytes(line.ToJsonString());

    // MARK: - Sessions

    /// <summary>A session's status, what it waits for and since when, as Claude Code wrote them.</summary>
    [Fact]
    public void ListsOpenSessionsWithTheirStatus()
    {
        var host = new HostApp("Visual Studio Code", 10196);
        WriteSession(LivePid, "s1", "evan-b6", @"C:\Users\me\EVAN", status: "waiting", extra: With(
            ("waitingFor", "permission prompt"), ("statusUpdatedAt", 1_793_467_940_000.0),
            ("startedAt", 1_793_460_000_000.0), ("entrypoint", "claude-vscode")));
        WriteSession(ClosedPid, "s2", "gone", @"C:\Users\me\Gone");

        var activity = Scanner(host).ScanActivity();

        var session = Assert.Single(activity.Sessions);
        Assert.Equal("s1", session.Id);
        Assert.Equal("evan-b6", session.Name);
        Assert.Equal(SessionState.Waiting, session.Status);
        Assert.Equal("permission prompt", session.WaitingFor);
        Assert.Equal("Permission prompt", session.WaitingReason);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_793_467_940), session.StatusSince);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_793_460_000), session.OpenedAt);
        Assert.Equal("EVAN", session.FolderName);
        Assert.Equal(host, session.Host);
        Assert.False(session.IsBackground);
        Assert.Empty(activity.Agents);
    }

    /// <summary>
    /// A session file as Claude Code writes it on Windows, from this PC: <c>procStart</c> is the
    /// kernel's FILETIME, and the key file beside it is no session.
    /// </summary>
    [Fact]
    public void ReadsAWindowsSessionFile()
    {
        var folder = Path.Combine(_root, "sessions");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "30872.json"), """
            {"pid":30872,"sessionId":"35432ee5-1c49-466e-8354-655abceb55b8","cwd":"g:\\Projet - Programation\\Claudio","startedAt":1794763850644,"procStart":"134392374497722792","version":"2.1.325","kind":"interactive","entrypoint":"claude-vscode","name":"claudio-3c","nameSource":"derived","status":"busy","updatedAt":1794763850901,"statusUpdatedAt":1794763850901}
            """);
        File.WriteAllText(Path.Combine(folder, "30872.9186c9db.key"), "secret");
        var checkedStarts = new List<(int, DateTimeOffset?)>();

        var activity = Scanner(isLive: (pid, start) => { checkedStarts.Add((pid, start)); return true; }).ScanActivity();

        var session = Assert.Single(activity.Sessions);
        Assert.Equal("claudio-3c", session.Name);
        Assert.Equal("Claudio", session.FolderName);
        Assert.Equal(SessionState.Busy, session.Status);
        Assert.Equal(DateTimeOffset.UnixEpoch.AddTicks(17_947_638_497_722_792), session.ProcessStartedAt);
        Assert.Equal([(30872, session.ProcessStartedAt)], checkedStarts);
        Assert.True(session.CanBeInterrupted);
    }

    /// <summary>A session open in two processes is one session.</summary>
    [Fact]
    public void SessionOpenTwiceIsListedOnce()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire", file: "copy.json");
        Assert.Equal(["s1"], Scanner().ScanActivity().Sessions.Select(session => session.Id));
    }

    /// <summary>The last thing typed: not a tool result, not what Claude Code adds in tags, not a meta line.</summary>
    [Fact]
    public void ReadsTheLastPromptOfASession()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        var path = WriteTranscript("s1",
            Typed("first question"),
            Typed(Blocks(("text", "text", "<ide_opened_file>a.ts</ide_opened_file>"),
                         ("text", "text", "add the allergens\n  filter to the menu page"))),
            Typed(new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = "t1", ["content"] = "ok" })),
            Typed("<command-name>/model</command-name>"),
            Typed("Caveat: this is meta", meta: true),
            Typed(Blocks(("text", "text", "[Request interrupted by user]"))));
        var scanner = Scanner();

        var activity = scanner.ScanActivity();
        Assert.Equal("add the allergens filter to the menu page", activity.Sessions[0].LastPrompt);

        // Appended since: read on the next pass, the rest of the file is not read again.
        File.AppendAllText(path, Jsonl(Typed("now ship it")));
        activity = scanner.ScanActivity();
        Assert.Equal("now ship it", activity.Sessions[0].LastPrompt);
    }

    /// <summary>What the session does itself, between two agents, is read from its own transcript.</summary>
    [Fact]
    public void ReadsTheSessionsOwnLastCall()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        WriteTranscript("s1",
            Typed("run the tests"),
            Tool("t1", "Read", With(("file_path", @"C:\x\a.ts")), Now.AddSeconds(-60)),
            Typed(new JsonArray(new JsonObject { ["type"] = "tool_result", ["tool_use_id"] = "t1", ["content"] = "ok" })),
            Tool("t2", "Bash", With(("command", "cd app && swift test")), Now.AddSeconds(-5)));

        var session = Scanner().ScanActivity().Sessions[0];

        Assert.Equal("run the tests", session.LastPrompt);
        Assert.Equal(AgentWork.Tests, session.LastAction?.Work);
        Assert.Equal("swift test", session.LastAction?.Detail);
    }

    /// <summary>Claude Code's own record of the last prompt is read too, and a compaction summary is not taken for one.</summary>
    [Fact]
    public void LastPromptLineAndCompactSummary()
    {
        Assert.Equal("Mettre en place !", AgentScanner.Prompt(Bytes(With(("type", "last-prompt"), ("lastPrompt", "Mettre en place !")))));
        var summary = Typed("This session is being continued from a previous conversation");
        summary["isCompactSummary"] = true;
        Assert.Null(AgentScanner.Prompt(Bytes(summary)));
    }

    /// <summary>A long prompt is cut: it only has to say which conversation it is.</summary>
    [Fact]
    public void LongPromptIsCut()
    {
        var prompt = AgentScanner.Prompt(Bytes(Typed(string.Concat(Enumerable.Repeat("word ", 100)))));
        Assert.Equal(AgentScanner.PromptLength + 1, prompt?.Length);
        Assert.EndsWith("…", prompt, StringComparison.Ordinal);
    }

    /// <summary>
    /// The app a session runs in, named as its users know it: a Mac takes the outermost bundle,
    /// Windows the program (the walk itself is in <see cref="ProcessIdentityTests"/>).
    /// </summary>
    [Fact]
    public void HostAppIsNamedAsItsUsersKnowIt()
    {
        Assert.Equal("Visual Studio Code", ProcessIdentity.HostName(@"C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe"));
        Assert.Equal("Terminal", ProcessIdentity.HostName(@"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_1.23.1\WindowsTerminal.exe"));
        Assert.Null(ProcessIdentity.HostName(@"C:\Program Files\Git\usr\bin\tmux.exe"));
    }

    // MARK: - Agents

    [Fact]
    public void ListsWhatALiveSessionLaunched()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        WriteAgent("s1", "a1", Running, [
            Prompt(Now.AddSeconds(-600)),
            Tool("t1", "Read", With(("file_path", @"C:\Users\me\Claire\a.ts")), Now.AddSeconds(-500)),
            Tool("t2", "Write", With(("file_path", @"C:\Users\me\Claire\banc.ts")), Now.AddSeconds(-20)),
        ], Now.AddSeconds(-20));

        var agent = Assert.Single(Scanner().Scan());

        Assert.Equal("a1", agent.Id);
        Assert.Equal("Make prescription file reading reliable", agent.Title);
        Assert.Equal("claire-bb", agent.SessionName);
        Assert.Equal(LivePid, agent.SessionPid);
        Assert.Equal(Now.AddSeconds(-600), agent.StartedAt);
        Assert.Equal(2, agent.ActionCount);
        Assert.Equal(["banc.ts"], agent.EditedFiles);
        Assert.Equal("banc.ts", agent.LastAction?.File);
        Assert.Equal(AgentActionKind.Edit, agent.LastAction?.Kind);
        Assert.Equal(AgentStatus.Working, agent.Status(Now));
    }

    /// <summary>
    /// The session file's start time reaches the liveness check, so a reused pid is told apart
    /// from the process that wrote the file.
    /// </summary>
    [Fact]
    public void SessionStartTimeReachesTheLivenessCheck()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire", procStart: "Mon Nov  9 22:26:03 2026");
        WriteAgent("s1", "a1", Running, [Prompt(Now)], Now);
        var seen = new List<DateTimeOffset?>();

        var agents = Scanner(isLive: (pid, start) => { seen.Add(start); return pid == LivePid; }).Scan();

        var expected = ProcessIdentity.DateFromProcStart("Mon Nov  9 22:26:03 2026");
        Assert.NotNull(expected);
        Assert.Equal([expected], seen);
        Assert.Equal(expected, agents[0].SessionStartedAt);
    }

    [Fact]
    public void IgnoresTheAgentsOfAClosedSession()
    {
        WriteSession(ClosedPid, "s1", "claire-bb", @"C:\Users\me\Claire");
        WriteAgent("s1", "a1", Running, [Prompt(Now)], Now);
        Assert.Empty(Scanner().Scan());
    }

    /// <summary>A finished agent stays ten minutes, marked done, then leaves the list.</summary>
    [Fact]
    public void FinishedAgentLingersTenMinutesThenLeaves()
    {
        var finished = (JsonObject)Running.DeepClone();
        finished["finished"] = true;
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        WriteAgent("s1", "recent", finished, [Prompt(Now.AddSeconds(-900))], Now.AddMinutes(-9));
        WriteAgent("s1", "old", finished, [Prompt(Now.AddSeconds(-1800))], Now.AddMinutes(-11));

        var agents = Scanner().Scan();

        Assert.Equal(["recent"], agents.Select(agent => agent.Id));
        Assert.Equal(AgentStatus.Done, agents[0].Status(Now));
    }

    /// <summary>Not finished but silent for half an hour: interrupted, not at work.</summary>
    [Fact]
    public void SilentAgentIsDropped()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        WriteAgent("s1", "a1", Running, [Prompt(Now.AddSeconds(-3600))], Now.AddMinutes(-31));
        Assert.Empty(Scanner().Scan());
    }

    /// <summary>Claude Code can write the same tool call on more than one line.</summary>
    [Fact]
    public void SameToolCallWrittenTwiceCountsOnce()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        var call = Tool("t1", "Bash", With(("command", "ls"), ("description", "List files")), Now.AddSeconds(-10));
        WriteAgent("s1", "a1", Running, [Prompt(Now.AddSeconds(-60)), call, (JsonObject)call.DeepClone()], Now.AddSeconds(-10));

        Assert.Equal(1, Scanner().Scan()[0].ActionCount);
    }

    [Fact]
    public void PicksUpWhatWasAppendedSinceTheLastPass()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        var transcript = WriteAgent("s1", "a1", Running, [
            Prompt(Now.AddSeconds(-120)),
            Tool("t1", "Read", With(("file_path", @"C:\x\a.ts")), Now.AddSeconds(-100)),
        ], Now.AddSeconds(-100));
        var scanner = Scanner();
        scanner.Scan();

        File.AppendAllText(transcript, Jsonl(Tool("t2", "Edit", With(("file_path", @"C:\x\b.ts")), Now.AddSeconds(-5))));
        File.SetLastWriteTimeUtc(transcript, Now.AddSeconds(-5).UtcDateTime);
        var agents = scanner.Scan();

        Assert.Equal(2, agents[0].ActionCount);
        Assert.Equal(["b.ts"], agents[0].EditedFiles);
    }

    /// <summary>The agent room reads what each call is for: the command a shell call ran, the skill loaded.</summary>
    [Fact]
    public void ReadsTheWorkBehindEachCall()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        WriteAgent("s1", "a1", Running, [
            Prompt(Now.AddSeconds(-120)),
            Tool("t1", "Edit", With(("file_path", @"C:\x\specs\001\plan.md")), Now.AddSeconds(-60)),
            Tool("t2", "Skill", With(("skill", "speckit-plan")), Now.AddSeconds(-40)),
            Tool("t3", "Bash", With(("command", "cd app && swift test --filter Scan")), Now.AddSeconds(-5)),
        ], Now.AddSeconds(-5));

        var agent = Assert.Single(Scanner().Scan());

        Assert.Equal([AgentWork.Plan, AgentWork.Skill, AgentWork.Tests], agent.Recent.Select(action => action.Work));
        Assert.Equal([null, "speckit-plan", "swift test"], agent.Recent.Select(action => action.Detail));
        Assert.Equal("tests · swift test", agent.WorkLabel);
    }

    /// <summary>A session resumed while its first process still runs has two pid files and one id.</summary>
    [Fact]
    public void SessionOpenTwiceListsItsAgentsOnce()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        WriteSession(LivePid + 1, "s1", "claire-bb", @"C:\Users\me\Claire");
        WriteAgent("s1", "a1", Running, [Prompt(Now)], Now);

        var agents = Scanner(isLive: (pid, _) => pid is LivePid or LivePid + 1).Scan();

        Assert.Equal(["a1"], agents.Select(agent => agent.Id));
    }

    [Fact]
    public void SessionWithoutANameTakesItsFolderName()
    {
        WriteSession(LivePid, "s1", null, @"C:\Users\me\EVAN");
        WriteAgent("s1", "a1", Running, [Prompt(Now)], Now);
        Assert.Equal("EVAN", Scanner().Scan()[0].SessionName);
    }

    [Fact]
    public void OldestAgentComesFirst()
    {
        WriteSession(LivePid, "s1", "claire-bb", @"C:\Users\me\Claire");
        WriteAgent("s1", "late", Running, [Prompt(Now.AddSeconds(-60))], Now);
        WriteAgent("s1", "early", Running, [Prompt(Now.AddSeconds(-600))], Now);
        Assert.Equal(["early", "late"], Scanner().Scan().Select(agent => agent.Id));
    }
}
