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

    [Theory]
    [InlineData(KillRefusal.IdentityChanged, "the process changed, nothing was killed")]
    [InlineData(KillRefusal.ProtectedProcess, "protected process")]
    [InlineData(KillRefusal.LiveClaudeSession, "live Claude session")]
    [InlineData(KillRefusal.SystemRefused, "refused by the system")]
    [InlineData(KillRefusal.SurvivedKill, "still alive after the kill")]
    public void ExplainsEveryRefusal(KillRefusal refusal, string expected) =>
        Assert.Equal(expected, PortsText.Explain(refusal));
}
