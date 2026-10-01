using Claudio.Core.Services;

namespace Claudio.Core.Tests;

public sealed class ProcessTableTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 1, 19, 30, 0, TimeSpan.Zero);

    /// <summary>
    /// A real tree, captured on 2026-10-01: Claude Code in VS Code on Windows runs its shell
    /// through cmd.exe, which starts PowerShell, which starts the server. VS Code's own parent is
    /// gone, and its PID with it.
    /// </summary>
    private static readonly RunningProcess[] Tree =
    [
        new(31000, 19784, Epoch.AddMinutes(49), @"""C:\Python313\python.exe"" -m http.server 8765"),
        new(19784, 29636, Epoch.AddMinutes(48), @"""C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe"" -NoProfile -NonInteractive -Command ..."),
        new(29636, 28892, Epoch.AddMinutes(48), @"C:\WINDOWS\System32\cmd.exe /d /s /c """"C:\WINDOWS\System32\chcp.com"" 65001 >nul & ..."""),
        new(28892, 29744, Epoch.AddMinutes(3), @"c:\Users\x\.vscode\extensions\anthropic.claude-code-2.1.286-win32-x64\resources\native-binary\claude.exe --output-format stream-json --verbose"),
        new(29744, 27560, Epoch.AddMinutes(3), @"""C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe"" --type=utility --utility-sub-type=node.mojom.NodeService"),
        new(27560, 28360, Epoch, @"""C:\Users\x\AppData\Local\Programs\Microsoft VS Code\Code.exe"""),
        new(5771, 4940, Epoch, "node.exe next-server"),
    ];

    private static ProcessTable Table(params RunningProcess[] processes) =>
        new(processes.ToDictionary(process => process.Pid));

    [Fact]
    public void WalksAncestorsUpToTheRoot() =>
        Assert.Equal([19784, 29636, 28892, 29744, 27560], Table(Tree).Ancestors(31000).Select(process => process.Pid));

    [Fact]
    public void FindsClaudeSessionRoot() => Assert.Equal(28892, Table(Tree).ClaudeSessionRoot(31000)?.Pid);

    [Fact]
    public void ProcessWithoutClaudeAncestorHasNoSessionRoot() => Assert.Null(Table(Tree).ClaudeSessionRoot(5771));

    [Fact]
    public void AnUnknownProcessHasNoAncestors() => Assert.Empty(Table(Tree).Ancestors(42));

    /// <summary>
    /// Windows keeps a dead parent's PID in its children and reissues it: a "parent" that started
    /// after its child is a stranger, and the walk stops before it.
    /// </summary>
    [Fact]
    public void StopsAtAParentThatStartedAfterItsChild()
    {
        var table = Table(
            new RunningProcess(500, 400, Epoch, "node.exe server.js"),
            new RunningProcess(400, 300, Epoch.AddHours(1), @"C:\Users\x\.local\bin\claude.exe"));
        Assert.Empty(table.Ancestors(500));
        Assert.Null(table.ClaudeSessionRoot(500));
    }

    /// <summary>A process the system would not describe has no start time; the walk goes on through it.</summary>
    [Fact]
    public void AnUnknownStartTimeDoesNotCutTheWalk()
    {
        var table = Table(
            new RunningProcess(500, 400, Epoch, "node.exe server.js"),
            new RunningProcess(400, 300, DateTimeOffset.MinValue, "cmd.exe"),
            new RunningProcess(300, 1, Epoch.AddHours(-1), @"C:\Users\x\.local\bin\claude.exe"));
        Assert.Equal(300, table.ClaudeSessionRoot(500)?.Pid);
    }

    [Fact]
    public void ListsGenuineChildrenOnly()
    {
        var table = Table(
            new RunningProcess(500, 1, Epoch, "node.exe"),
            new RunningProcess(502, 500, Epoch.AddSeconds(2), "node.exe worker"),
            new RunningProcess(501, 500, Epoch.AddSeconds(1), "esbuild.exe"),
            new RunningProcess(503, 500, Epoch.AddSeconds(-5), "older.exe"));
        Assert.Equal([501, 502], table.Children(500).Select(process => process.Pid));
    }

    [Theory]
    [InlineData("/Users/x/.claude/local/claude")]
    [InlineData("/Users/x/.vscode/extensions/anthropic.claude-code-2.1.257/native-binary/claude --debug")]
    [InlineData("/usr/local/lib/node_modules/@anthropic-ai/claude-code/cli.js")]
    [InlineData(@"c:\Users\x\.vscode\extensions\anthropic.claude-code-2.1.286-win32-x64\resources\native-binary\claude.exe --output-format stream-json")]
    [InlineData(@"""C:\Users\Ada Lovelace\.local\bin\claude.exe"" --resume")]
    [InlineData(@"C:\Users\x\.local\bin\Claude.EXE")]
    [InlineData(@"""C:\Program Files\nodejs\node.exe"" C:\Users\x\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\cli.js")]
    [InlineData(@"node.exe --no-warnings ""C:\Users\x\AppData\Roaming\npm\node_modules\@anthropic-ai/claude-code/cli.js"" -p hi")]
    [InlineData(@"C:\Users\x\.bun\bin\bun.exe C:\Users\x\.bun\install\global\node_modules\@anthropic-ai\claude-code\cli.js")]
    public void RecognisesClaudeBinaries(string arguments) => Assert.True(ProcessTable.IsClaudeBinary(arguments));

    [Theory]
    [InlineData("/usr/bin/claudia")]
    [InlineData("/bin/zsh -c echo claude")]
    [InlineData(@"C:\WINDOWS\System32\WindowsPowerShell\v1.0\powershell.exe -Command claude")]
    [InlineData(@"C:\WINDOWS\System32\cmd.exe /c claude.cmd --resume")]
    [InlineData(@"""C:\Program Files\nodejs\node.exe"" server.js --name @anthropic-ai/claude-code")]
    [InlineData(@"node.exe -e ""require('claude')""")]
    [InlineData(@"C:\tools\claude-helper.exe")]
    [InlineData("")]
    public void TellsOtherProgramsApart(string arguments) => Assert.False(ProcessTable.IsClaudeBinary(arguments));

    /// <summary>A malformed table must not hang the scan.</summary>
    [Fact]
    public void CyclicParentDoesNotLoop()
    {
        var table = Table(new RunningProcess(10, 11, Epoch, "a.exe"), new RunningProcess(11, 10, Epoch, "b.exe"));
        Assert.True(table.Ancestors(10).Count < 70);
    }

    [Fact]
    public void ADeepChainIsCapped()
    {
        var chain = Enumerable.Range(100, 200).Select(pid => new RunningProcess(pid, pid + 1, Epoch, "sh.exe")).ToArray();
        Assert.Equal(64, Table(chain).Ancestors(100).Count);
    }
}
