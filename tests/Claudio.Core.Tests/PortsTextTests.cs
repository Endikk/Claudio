using Claudio.Core.Models;
using Claudio.Core.Presentation;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

public sealed class PortsTextTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 19, 30, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(45, "45 s")]
    [InlineData(3 * 60, "3 min")]
    [InlineData(5 * 3600, "5 h")]
    [InlineData(2 * 86400, "2 d")]
    [InlineData(-30, "0 s")]
    public void AgeReadsInTheLargestUsefulUnit(int seconds, string expected) =>
        Assert.Equal(expected, PortsText.Age(Now.AddSeconds(-seconds), Now));

    [Fact]
    public void SubtitleNamesTheProjectWhenThereIsOne()
    {
        var port = new ListeningPort("500-4000", 500, 4000, "python.exe", "Surikat", Now.AddMinutes(-4), PortAttribution.Orphan, null);
        Assert.Equal("Surikat · 4 min", PortsText.Subtitle(port, Now));
        Assert.Equal("4 min", PortsText.Subtitle(port with { ProjectName = null }, Now));
    }

    private static ListeningPort Port(int pid, PortAttribution attribution, int number = 4000) =>
        new($"{pid}-{number}", pid, number, "python.exe", "Surikat", Now, attribution, null);

    private static McpOrphan Server(int pid, string name, ulong megabytes = 100) =>
        new($"mcp-{pid}", pid, Now, name, "GoodFood", megabytes << 20);

    /// <summary>The tab's badge counts everything Claude left behind, ports and servers alike.</summary>
    [Fact]
    public void OrphanCountIncludesMcpServers()
    {
        Assert.Equal(3, PortsText.OrphanCount([Port(500, PortAttribution.Orphan)], [Server(600, "chrome-devtools-mcp"), Server(700, "figma-mcp")]));
        Assert.Equal(1, PortsText.OrphanCount([], [Server(600, "chrome-devtools-mcp")]));
    }

    /// <summary>The confirmation names what it is about to kill: ports by number, servers by name.</summary>
    [Fact]
    public void BulkTargetsNamePortsThenServers() =>
        Assert.Equal("3000, chrome-devtools-mcp, figma-mcp",
                     PortsText.BulkKillTargets([Port(500, PortAttribution.Orphan, 3000)], [Server(600, "chrome-devtools-mcp"), Server(700, "figma-mcp")]));

    [Fact]
    public void McpMemoryAddsUpEveryServer() =>
        Assert.Equal(713UL << 20, PortsText.McpMemory([Server(600, "chrome-devtools-mcp", 412), Server(700, "figma-mcp", 301)]));

    [Fact]
    public void MemoryReadsInMegabytesThenGigabytes()
    {
        Assert.Equal("412 MB", PortsText.Memory(412UL << 20));
        Assert.Equal("0 MB", PortsText.Memory(0));
        // printf rounds half to even, as Claudy's %.1f: 1280 MB is 1.25 GB, read "1.2 GB".
        Assert.Equal("1.2 GB", PortsText.Memory(1280UL << 20));
        Assert.Equal("1.1 GB", PortsText.Memory(1_101UL << 20));
        Assert.Equal("1.0 GB", PortsText.Memory((1_023UL << 20) + (900UL << 10)));
    }

    [Fact]
    public void AServersSubtitleNamesItsProject()
    {
        var server = Server(600, "chrome-devtools-mcp") with { StartedAt = Now.AddHours(-3) };
        Assert.Equal("GoodFood · 3 h", PortsText.Subtitle(server, Now));
        Assert.Equal("3 h", PortsText.Subtitle(server with { ProjectName = null }, Now));
    }

    /// <summary>A process already listed by port must not come back as a server; a scan that could not run lists none.</summary>
    [Fact]
    public void TheMcpScanSkipsThePidsListedAsPorts()
    {
        var ready = new PortScanState.Ready([Port(500, PortAttribution.Orphan), Port(501, PortAttribution.Live)], false);
        Assert.Equal([500, 501], ready.ListedPids.Order());
        Assert.Empty(new PortScanState.Unavailable("the TCP table is unreadable").ListedPids);
        Assert.Empty(PortScanState.Scanning.Instance.ListedPids);
    }

    [Theory]
    [InlineData(KillRefusal.IdentityChanged, "the process changed, nothing was killed")]
    [InlineData(KillRefusal.ProtectedProcess, "protected process")]
    [InlineData(KillRefusal.LiveClaudeSession, "live Claude session")]
    [InlineData(KillRefusal.SystemRefused, "refused by the system")]
    [InlineData(KillRefusal.SurvivedKill, "still alive after the kill")]
    public void ExplainsEveryRefusal(KillRefusal refusal, string expected) =>
        Assert.Equal(expected, PortsText.Explain(refusal));
}
