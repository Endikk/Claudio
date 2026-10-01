using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

public sealed class PortScannerTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 1, 19, 30, 0, TimeSpan.Zero);
    private static readonly ProcessEnvironment.Markers Claude = new(true, null);
    private static readonly ProcessEnvironment.Markers Stranger = new(false, null);

    private static ProcessTable Table(params RunningProcess[] processes) =>
        new(processes.ToDictionary(process => process.Pid));

    private static readonly RunningProcess Session =
        new(8859, 1, Epoch, @"C:\Users\x\.local\bin\claude.exe");

    /// <summary>
    /// A dev server on <c>localhost</c> listens on 127.0.0.1 and on ::1, and Windows reports
    /// both. Left as is, two rows would share one id.
    /// </summary>
    [Fact]
    public void CollapsesSocketsOnTheSamePort()
    {
        var listeners = PortScanner.Collapse(
        [
            new Listener(1271, "node.exe", 5173, "127.0.0.1"),
            new Listener(1271, "node.exe", 5173, "::1"),
            new Listener(1271, "node.exe", 24678, "0.0.0.0"),
            new Listener(1300, "python.exe", 5173, "::"),
        ]);
        Assert.Equal([(1271, 5173, "127.0.0.1"), (1271, 24678, "0.0.0.0"), (1300, 5173, "::")],
                     listeners.Select(listener => (listener.Pid, listener.Port, listener.Address)));
    }

    [Theory]
    [InlineData("OrbStack")]
    [InlineData("com.docker.backend.exe")]
    [InlineData("Docker Desktop.exe")]
    [InlineData("vpnkit.exe")]
    [InlineData("wslrelay.exe")]
    [InlineData("podman.exe")]
    public void DeniesContainerRuntimes(string command) => Assert.True(PortScanner.IsDenied(command));

    [Fact]
    public void DoesNotDenyADevServer() => Assert.False(PortScanner.IsDenied("node.exe"));

    [Fact]
    public void AttributesLivePortWhenClaudeAncestorIsAlive()
    {
        var (ports, _) = PortScanner.Attribute(
            [new Listener(46694, "python.exe", 4000, "127.0.0.1")],
            Table(new RunningProcess(46694, 8859, Epoch, "python.exe -m http.server"), Session),
            _ => new ProcessEnvironment.Markers(true, @"C:\Users\x\Dev\Surikat"));
        var port = Assert.Single(ports);
        Assert.Equal(PortAttribution.Live, port.Attribution);
        Assert.Equal(8859, port.SessionRootPid);
        Assert.Equal("Surikat", port.ProjectName);
        Assert.Equal(Epoch, port.StartedAt);
    }

    [Fact]
    public void AttributesOrphanWhenClaudeAncestorIsGone()
    {
        var (ports, _) = PortScanner.Attribute(
            [new Listener(95778, "bun.exe", 37701, "127.0.0.1")],
            Table(new RunningProcess(95778, 1, Epoch, "bun.exe worker")),
            _ => Claude);
        Assert.Equal(PortAttribution.Orphan, ports[0].Attribution);
        Assert.Null(ports[0].SessionRootPid);
    }

    [Fact]
    public void DropsPortsWithoutMarkers()
    {
        var (ports, _) = PortScanner.Attribute(
            [new Listener(5771, "node.exe", 3000, "0.0.0.0")],
            Table(new RunningProcess(5771, 8859, Epoch, "node.exe next-server"), Session),
            _ => Stranger);
        Assert.Empty(ports);
    }

    /// <summary>
    /// A container runtime publishes ports on behalf of everything it hosts: killing it would
    /// take the whole runtime down, so it is never attributed whatever its environment says.
    /// </summary>
    [Fact]
    public void DropsDeniedCommandsEvenWithMarkers()
    {
        var (ports, _) = PortScanner.Attribute(
            [new Listener(40964, "com.docker.backend.exe", 3001, "0.0.0.0")],
            Table(new RunningProcess(40964, 1, Epoch, "com.docker.backend.exe")),
            _ => Claude);
        Assert.Empty(ports);
    }

    /// <summary>Between the two reads the process may have exited: nothing is known of it.</summary>
    [Fact]
    public void DropsListenersMissingFromTheTable()
    {
        var (ports, isDegraded) = PortScanner.Attribute([new Listener(700, "node.exe", 3000, "::")], Table(), _ => Claude);
        Assert.Empty(ports);
        Assert.False(isDegraded);
    }

    [Fact]
    public void IdentityCombinesPidAndPort()
    {
        var (ports, _) = PortScanner.Attribute(
            [new Listener(46694, "python.exe", 4000, "127.0.0.1")],
            Table(new RunningProcess(46694, 1, Epoch, "python.exe")),
            _ => Claude);
        Assert.Equal("46694-4000", ports[0].Id);
    }

    /// <summary>
    /// When an environment cannot be read, attribution falls back to the process tree: a live
    /// session is still recognisable, so the tab keeps working for the common case.
    /// </summary>
    [Fact]
    public void FallsBackToAncestryWhenEnvironmentIsUnreadable()
    {
        var (ports, isDegraded) = PortScanner.Attribute(
            [new Listener(46694, "python.exe", 4000, "127.0.0.1")],
            Table(new RunningProcess(46694, 8859, Epoch, "python.exe -m http.server"), Session),
            _ => null);
        var port = Assert.Single(ports);
        Assert.Equal(PortAttribution.Live, port.Attribution);
        Assert.Null(port.ProjectName);
        Assert.True(isDegraded);
    }

    /// <summary>The fallback cannot see orphans: that is exactly what the degraded flag warns about.</summary>
    [Fact]
    public void FallbackDropsOrphans()
    {
        var (ports, isDegraded) = PortScanner.Attribute(
            [new Listener(95778, "bun.exe", 37701, "127.0.0.1")],
            Table(new RunningProcess(95778, 1, Epoch, "bun.exe worker")),
            _ => null);
        Assert.Empty(ports);
        Assert.True(isDegraded);
    }

    [Fact]
    public void ReadsEachEnvironmentOnce()
    {
        var reads = 0;
        var (ports, _) = PortScanner.Attribute(
            [new Listener(500, "node.exe", 5173, "::1"), new Listener(500, "node.exe", 24678, "::1")],
            Table(new RunningProcess(500, 1, Epoch, "node.exe vite")),
            _ =>
            {
                reads++;
                return Claude;
            });
        Assert.Equal(2, ports.Count);
        Assert.Equal(1, reads);
    }

    [Theory]
    [InlineData(@"C:\Users\x\Dev\Surikat", "Surikat")]
    [InlineData(@"C:\Users\x\Dev\Surikat\", "Surikat")]
    [InlineData("/Users/x/Dev/Surikat", "Surikat")]
    [InlineData(@"\\wsl.localhost\Ubuntu\home\x\surikat/", "surikat")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void NamesTheProjectAfterItsFolder(string? directory, string? expected) =>
        Assert.Equal(expected, PortScanner.ProjectName(directory));

    [Fact]
    public void ScanSortsByPortAndCollapses()
    {
        var scanner = new PortScanner(
            () => [new Listener(600, "node.exe", 5173, "::1"), new Listener(500, "python.exe", 4000, "127.0.0.1"),
                   new Listener(600, "node.exe", 5173, "127.0.0.1")],
            () => Table(new RunningProcess(500, 1, Epoch, "python.exe"), new RunningProcess(600, 1, Epoch, "node.exe")),
            _ => Claude);
        var ready = Assert.IsType<PortScanState.Ready>(scanner.Scan());
        Assert.Equal([4000, 5173], ready.Ports.Select(port => port.Port));
        Assert.False(ready.IsDegraded);
    }

    [Fact]
    public void ScanIsUnavailableWithoutTheTcpTable() =>
        Assert.IsType<PortScanState.Unavailable>(new PortScanner(() => null, () => Table(), _ => Claude).Scan());

    [Fact]
    public void ScanIsUnavailableWithoutTheProcessList() =>
        Assert.IsType<PortScanState.Unavailable>(new PortScanner(() => [], () => null, _ => Claude).Scan());

    /// <summary>Two scans that found the same ports read as the same state.</summary>
    [Fact]
    public void ReadyStatesCompareTheirPorts()
    {
        ListeningPort Port() => new("500-4000", 500, 4000, "python.exe", "Surikat", Epoch, PortAttribution.Orphan, null);
        Assert.Equal(new PortScanState.Ready([Port()], false), new PortScanState.Ready([Port()], false));
        Assert.NotEqual(new PortScanState.Ready([Port()], false), new PortScanState.Ready([Port()], true));
        Assert.NotEqual<PortScanState>(new PortScanState.Ready([], false), PortScanState.Scanning.Instance);
    }
}
