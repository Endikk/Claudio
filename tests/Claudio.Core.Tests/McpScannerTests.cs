using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>The orphan rule against injected processes: no test reads this machine's.</summary>
public sealed class McpScannerTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 1, 19, 30, 0, TimeSpan.Zero);
    private const ulong Megabyte = 1UL << 20;

    private static readonly ProcessEnvironment.Markers ClaudeEnvironment = new(true, @"C:\Users\x\Dev\GoodFood", "s-dead");

    private const string Node = @"""C:\Program Files\nodejs\node.exe""";

    /// <summary>Started a minute after the epoch, so a parent at the epoch is a real parent.</summary>
    private static RunningProcess Process(int pid, int parent, string arguments, double minutes = 1) =>
        new(pid, parent, Epoch.AddMinutes(minutes), arguments);

    private static ProcessTable Table(params RunningProcess[] processes) => new(processes.ToDictionary(process => process.Pid));

    /// <summary>
    /// A PC where every process is the user's and runs Node from Program Files, unless told
    /// otherwise. <paramref name="reads"/> records which environments the scan asked for, to prove
    /// the expensive read stays behind the cheap checks.
    /// </summary>
    private static ProcessProbe Probe(
        Dictionary<int, ProcessEnvironment.Markers> markers,
        Dictionary<int, ulong>? memory = null,
        HashSet<int>? strangers = null,
        Dictionary<int, string>? executables = null,
        List<int>? reads = null) =>
        new(
            pid => !(strangers ?? []).Contains(pid),
            pid => executables is not null && executables.TryGetValue(pid, out var path) ? path : @"C:\Program Files\nodejs\node.exe",
            pid =>
            {
                reads?.Add(pid);
                return markers.GetValueOrDefault(pid);
            },
            pid => memory is not null && memory.TryGetValue(pid, out var bytes) ? bytes : null);

    private static IReadOnlyList<McpOrphan> Orphans(RunningProcess[] processes, ProcessProbe probe,
                                                    HashSet<int>? ports = null, HashSet<string>? live = null) =>
        McpScanner.Orphans(Table(processes), ports ?? [], probe, () => live ?? []);

    // The orphan rule

    /// <summary>Claude Code is gone: its PID (8859) is in no process of the snapshot.</summary>
    [Fact]
    public void ListsAServerWhoseSessionIsGone()
    {
        var result = Orphans([Process(4100, 8859, @"C:\WINDOWS\system32\cmd.exe /d /s /c ""npx -y chrome-devtools-mcp@latest""")],
                             Probe(new() { [4100] = ClaudeEnvironment }, memory: new() { [4100] = 52 * Megabyte }));
        Assert.Equal([new McpOrphan("mcp-4100", 4100, Epoch.AddMinutes(1), "chrome-devtools-mcp", "GoodFood", 52 * Megabyte)], result);
    }

    /// <summary>
    /// Windows hands a dead parent's PID to later processes: a process holding that PID that
    /// started after the server is not its parent, and the server is still an orphan.
    /// </summary>
    [Fact]
    public void AParentPidReissuedLaterIsNotAParent()
    {
        var result = Orphans(
            [Process(4100, 8859, "uvx mcp-server-fetch"), Process(8859, 1, "notepad.exe", minutes: 30)],
            Probe(new() { [4100] = ClaudeEnvironment }));
        Assert.Equal([4100], result.Select(orphan => orphan.Pid));
    }

    /// <summary>
    /// The claude-mem worker lost its parent and carries <c>CLAUDE_PROJECT_DIR</c>, but it is a
    /// daemon on purpose: nothing in its command line says MCP, so it is never listed.
    /// </summary>
    [Fact]
    public void SkipsTheClaudeMemDaemon()
    {
        var reads = new List<int>();
        var daemon = Process(5200, 1, @"bun C:\Users\x\.claude\plugins\marketplaces\thedotmack\plugin\scripts\worker-service.cjs --daemon");
        Assert.Empty(Orphans([daemon], Probe(new() { [5200] = ClaudeEnvironment }, reads: reads)));
        Assert.Empty(reads);
    }

    /// <summary>
    /// The claude-mem daemon runs its own <c>chroma-mcp</c> and passes it its Claude markers. The
    /// server is not left behind while the daemon that manages it lives: only a process whose
    /// parent is gone is.
    /// </summary>
    [Fact]
    public void SkipsAServerADaemonStillManages()
    {
        var daemon = new ProcessEnvironment.Markers(true, @"C:\Users\x\Dev\Claudy");
        var result = Orphans(
            [
                Process(21077, 1, @"C:\Users\x\.bun\bin\bun.exe C:\Users\x\.claude\plugins\scripts\worker-service.cjs --daemon", 0),
                Process(21624, 21077, @"C:\Users\x\.local\bin\uv.exe tool uvx --from chroma-mcp==0.2.6 chroma-mcp --client-type persistent"),
                Process(21627, 21624, @"C:\Users\x\AppData\Roaming\uv\tools\chroma-mcp\Scripts\python.exe C:\Users\x\.local\bin\chroma-mcp", 2),
            ],
            Probe(new() { [21077] = daemon, [21624] = daemon, [21627] = daemon }));
        Assert.Empty(result);
    }

    /// <summary>
    /// A session can be alive without being an ancestor of the server it launched. Its id is then
    /// the only thing that says the server is still in use.
    /// </summary>
    [Fact]
    public void SkipsAServerWhoseSessionIsStillLive()
    {
        var live = new ProcessEnvironment.Markers(true, null, "s-live");
        var result = Orphans(
            [Process(4100, 1, "npx chrome-devtools-mcp@latest"), Process(4200, 1, "uvx mcp-server-fetch")],
            Probe(new() { [4100] = live, [4200] = ClaudeEnvironment }),
            live: ["s-live"]);
        Assert.Equal([4200], result.Select(orphan => orphan.Pid));
    }

    [Fact]
    public void AServerWithoutSessionIdIsAnOrphan()
    {
        var result = Orphans([Process(4200, 1, "uvx mcp-server-fetch")],
                             Probe(new() { [4200] = new ProcessEnvironment.Markers(true, null) }),
                             live: ["s-live"]);
        Assert.Equal([4200], result.Select(orphan => orphan.Pid));
        Assert.Null(result[0].ProjectName);
    }

    [Fact]
    public void SkipsAServerUnderALiveClaudeSession()
    {
        var reads = new List<int>();
        var result = Orphans(
            [Process(8859, 1, @"C:\Users\x\.local\bin\claude.exe", 0), Process(4100, 8859, "npx chrome-devtools-mcp@latest")],
            Probe(new() { [4100] = ClaudeEnvironment }, reads: reads));
        Assert.Empty(result);
        Assert.Empty(reads);
    }

    /// <summary>
    /// <c>cmd /c npx</c> starts npx's Node, which starts the server's: three processes that all
    /// look like the server. Only the top one is a row, and it weighs what the whole tree holds,
    /// including the browser the server drives.
    /// </summary>
    [Fact]
    public void ListsOneRowPerTreeAndSumsItsMemory()
    {
        var result = Orphans(
            [
                Process(4100, 8859, @"C:\WINDOWS\system32\cmd.exe /d /s /c ""npx -y chrome-devtools-mcp@latest"""),
                Process(4101, 4100, $@"{Node} ""C:\Program Files\nodejs\node_modules\npm\bin\npx-cli.js"" -y chrome-devtools-mcp@latest", 2),
                Process(4102, 4101, $@"{Node} C:\Users\x\AppData\Local\npm-cache\_npx\1a2b\node_modules\chrome-devtools-mcp\build\src\index.js", 3),
                Process(4103, 4102, @"""C:\Program Files\Google\Chrome\Application\chrome.exe"" --headless", 4),
            ],
            Probe(new() { [4100] = ClaudeEnvironment, [4101] = ClaudeEnvironment, [4102] = ClaudeEnvironment },
                  memory: new() { [4100] = 4 * Megabyte, [4101] = 30 * Megabyte, [4102] = 120 * Megabyte, [4103] = 258 * Megabyte }));
        Assert.Equal([4100], result.Select(orphan => orphan.Pid));
        Assert.Equal(412 * Megabyte, result[0].ResidentBytes);
    }

    /// <summary>A child that only reuses the server's PID as its parent, having started before it, is not part of its tree.</summary>
    [Fact]
    public void AStaleChildDoesNotWeighOnTheTree()
    {
        var result = Orphans(
            [Process(4100, 1, "uvx mcp-server-fetch"), Process(4101, 4100, "python.exe", minutes: -5)],
            Probe(new() { [4100] = ClaudeEnvironment }, memory: new() { [4100] = Megabyte, [4101] = 900 * Megabyte }));
        Assert.Equal(Megabyte, Assert.Single(result).ResidentBytes);
    }

    [Fact]
    public void SkipsProcessesWithoutClaudeMarkers() =>
        Assert.Empty(Orphans([Process(4200, 1, "uvx mcp-server-fetch")],
                             Probe(new() { [4200] = new ProcessEnvironment.Markers(false, null) })));

    [Fact]
    public void SkipsUnreadableEnvironments() =>
        Assert.Empty(Orphans([Process(4200, 1, "uvx mcp-server-fetch")], Probe(new())));

    /// <summary>A server that also listens on a port is already a row in the ports list.</summary>
    [Fact]
    public void SkipsPidsListedAsPorts() =>
        Assert.Empty(Orphans([Process(4200, 1, "uvx mcp-server-fetch")], Probe(new() { [4200] = ClaudeEnvironment }), ports: [4200]));

    [Fact]
    public void SkipsAnotherUsersProcesses() =>
        Assert.Empty(Orphans([Process(4200, 1, "uvx mcp-server-fetch")],
                             Probe(new() { [4200] = ClaudeEnvironment }, strangers: [4200])));

    /// <summary>An installed app ships its own MCP helpers and manages their lifetime itself.</summary>
    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Cursor.Cursor_1.2.0.0_x64__abc\resources\mcp-helper.exe")]
    [InlineData(@"C:\Users\x\AppData\Local\AnthropicClaude\app-0.14.10\resources\mcp-runtime.exe")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\cursor\resources\app\bin\mcp-helper.exe")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Foo\resources\app.asar.unpacked\mcp\server.exe")]
    public void SkipsExecutablesInsideAnInstalledApp(string executable)
    {
        Assert.True(McpScanner.IsInstalledAppBinary(executable));
        Assert.Empty(Orphans([Process(4300, 1, $"\"{executable}\"")],
                             Probe(new() { [4300] = ClaudeEnvironment }, executables: new() { [4300] = executable })));
    }

    /// <summary>Node and Python live in Program Files, and they run most servers: that alone is no app.</summary>
    [Theory]
    [InlineData(@"C:\Program Files\nodejs\node.exe")]
    [InlineData(@"C:\Program Files\Python313\python.exe")]
    [InlineData(@"C:\WINDOWS\system32\cmd.exe")]
    [InlineData(@"C:\Users\x\.local\bin\uv.exe")]
    [InlineData(@"C:\Users\x\AppData\Local\Programs\Python\Python313\python.exe")]
    public void ARuntimeIsNoInstalledApp(string executable) => Assert.False(McpScanner.IsInstalledAppBinary(executable));

    /// <summary><c>claude mcp serve</c> is Claude Code itself, which the reaper never kills.</summary>
    [Fact]
    public void SkipsClaudeCodeServingMcp() =>
        Assert.Empty(Orphans([Process(4400, 1, @"C:\Users\x\.local\bin\claude.exe mcp serve")],
                             Probe(new() { [4400] = ClaudeEnvironment })));

    /// <summary>Reading an environment means copying it out of the process: only what already looks like an orphaned server gets one.</summary>
    [Fact]
    public void ReadsEnvironmentsOnlyAfterTheCheapChecks()
    {
        var reads = new List<int>();
        _ = Orphans(
            [
                Process(8859, 1, @"C:\Users\x\.local\bin\claude.exe", 0),
                Process(4100, 8859, "npx chrome-devtools-mcp@latest"),
                Process(4200, 1, "uvx mcp-server-fetch"),
                Process(4300, 1, "python.exe -m http.server"),
                Process(4400, 1, @"node.exe C:\x\mcp-server.js"),
            ],
            Probe(new() { [4200] = ClaudeEnvironment }, reads: reads),
            ports: [4400]);
        Assert.Equal([4200], reads);
    }

    [Fact]
    public void ReadsLiveSessionsOnlyWhenSomethingIsAttributed()
    {
        var asked = false;
        _ = McpScanner.Orphans(Table(Process(4300, 1, "python.exe -m http.server")), new HashSet<int>(), Probe(new()), () =>
        {
            asked = true;
            return new HashSet<string>();
        });
        Assert.False(asked);
    }

    [Fact]
    public void SortsTheHeaviestFirst()
    {
        var result = Orphans(
            [Process(4100, 1, "uvx mcp-server-fetch"), Process(4200, 1, "npx figma-mcp")],
            Probe(new() { [4100] = ClaudeEnvironment, [4200] = ClaudeEnvironment },
                  memory: new() { [4100] = 10 * Megabyte, [4200] = 301 * Megabyte }));
        Assert.Equal(["figma-mcp", "mcp-server-fetch"], result.Select(orphan => orphan.Name));
    }

    [Fact]
    public void AnUnreadableTableListsNothing() =>
        Assert.Empty(new McpScanner(() => null, Probe(new()), () => new HashSet<string>()).Scan(new HashSet<int>()));

    // Command line

    [Fact]
    public void FlagsDoNotMakeAnMcpServer()
    {
        Assert.False(McpScanner.LooksLikeMcpServer(@"node build\cli.js --mcp-config=C:\x\servers.json"));
        Assert.False(McpScanner.LooksLikeMcpServer(@"cmd.exe /c ""node build\cli.js --mcp-config=C:\x\servers.json"""));
        Assert.True(McpScanner.LooksLikeMcpServer("python -m mcp_server_git"));
        Assert.True(McpScanner.LooksLikeMcpServer(@"npx.cmd -y @modelcontextprotocol/server-filesystem C:\Users\x"));
        Assert.False(McpScanner.LooksLikeMcpServer("bun worker-service.cjs --daemon"));
    }

    [Theory]
    [InlineData("npm exec chrome-devtools-mcp@latest", "chrome-devtools-mcp")]
    [InlineData(@"node C:\x\node_modules\chrome-devtools-mcp\build\src\index.js", "chrome-devtools-mcp")]
    [InlineData("node /x/node_modules/chrome-devtools-mcp/build/src/index.js", "chrome-devtools-mcp")]
    [InlineData("uvx mcp-server-fetch", "mcp-server-fetch")]
    [InlineData("python -m mcp_server_git", "mcp_server_git")]
    [InlineData(@"C:\WINDOWS\system32\cmd.exe /d /s /c ""npx -y chrome-devtools-mcp@latest""", "chrome-devtools-mcp")]
    [InlineData(@"C:\Users\x\AppData\Roaming\npm\chrome-devtools-mcp.cmd", "chrome-devtools-mcp")]
    [InlineData(@"""C:\Program Files\nodejs\node.exe"" ""C:\Users\x\AppData\Roaming\npm\node_modules\@modelcontextprotocol\server-filesystem\dist\index.js"" C:\Users\x", "@modelcontextprotocol/server-filesystem")]
    public void NamesTheServerAfterItsPackage(string arguments, string name) =>
        Assert.Equal(name, McpScanner.DisplayName(arguments));

    /// <summary>
    /// uvx pins a version the Python way, and claude-mem launches its server from an inline script:
    /// the name is the run of name characters that holds the signature.
    /// </summary>
    [Fact]
    public void CutsTheNameOutOfPinsAndInlineScripts()
    {
        Assert.Equal("chroma-mcp", McpScanner.DisplayName(@"C:\Users\x\.local\bin\uv.exe tool uvx --from chroma-mcp==0.2.6 chroma-mcp"));
        Assert.Equal("mcp-server", McpScanner.DisplayName(
            """node -e const R=1;c.spawn(process.execPath,[p.join(R,'scripts',"mcp-server.cjs")],{stdio:'inherit'})"""));
    }

    [Theory]
    [InlineData(@"bun C:\x\plugin\scripts\mcp-server.cjs", "mcp-server")]
    [InlineData(@"node C:\x\figma-mcp.mjs", "figma-mcp")]
    [InlineData(@"python3 C:\x\notes_mcp.py --stdio", "notes_mcp")]
    [InlineData(@"C:\x\bin\github-mcp-server.exe stdio", "github-mcp-server")]
    public void DropsScriptExtensions(string arguments, string name) =>
        Assert.Equal(name, McpScanner.DisplayName(arguments));

    /// <summary>A scope alone names no server, and a package alone can be as bare as <c>mcp</c>: a scoped package keeps both halves.</summary>
    [Fact]
    public void KeepsTheScopeOfAScopedPackage()
    {
        Assert.Equal("@playwright/mcp", McpScanner.DisplayName("npx -y @playwright/mcp@latest"));
        Assert.Equal("@modelcontextprotocol/server-filesystem",
                     McpScanner.DisplayName(@"npx -y @modelcontextprotocol/server-filesystem@2025.1.0 C:\Users\x"));
    }

    [Fact]
    public void FallsBackToTheExecutableName()
    {
        Assert.Equal("python3", McpScanner.DisplayName("/opt/homebrew/bin/python3 server.py"));
        Assert.Equal("python.exe", McpScanner.DisplayName(@"""C:\Program Files\Python313\python.exe"" server.py"));
    }
}
