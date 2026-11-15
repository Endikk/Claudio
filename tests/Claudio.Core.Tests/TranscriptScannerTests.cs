using System.Globalization;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>The scanner's own cases, on transcripts written to a temporary folder.</summary>
public sealed class TranscriptScannerTests : IDisposable
{
    private readonly string _projects = Path.Combine(Path.GetTempPath(), $"claudio-scan-{Guid.NewGuid():N}");
    private readonly DateTimeOffset _now = DateTimeOffset.UtcNow;

    public void Dispose()
    {
        if (Directory.Exists(_projects))
        {
            Directory.Delete(_projects, recursive: true);
        }
    }

    /// <summary>The CSV export prices each counter on its own, so the scanner keeps them apart as well as adding them up.</summary>
    [Fact]
    public void EachCounterIsKeptApart()
    {
        Write(Line(input: 1, output: 20, cacheWrite: 300, cacheRead: 4_000));

        var entry = Assert.Single(new TranscriptScanner(() => [_projects], () => _now).Scan());

        Assert.Equal(1, entry.InputTokens);
        Assert.Equal(20, entry.OutputTokens);
        Assert.Equal(300, entry.CacheWriteTokens);
        Assert.Equal(4_000, entry.CacheReadTokens);
        // The total stays the sum of the four.
        Assert.Equal(4_321, entry.Tokens);
    }

    [Fact]
    public void StreamedResponseKeepsTheCountersOfItsFinalLine()
    {
        Write(Line(input: 3, output: 4, cacheWrite: 36_146), Line(input: 3, output: 202, cacheWrite: 36_146));

        var entry = Assert.Single(new TranscriptScanner(() => [_projects], () => _now).Scan());

        Assert.Equal(202, entry.OutputTokens);
        Assert.Equal(3, entry.InputTokens);
        Assert.Equal(36_146, entry.CacheWriteTokens);
    }

    private void Write(params string[] lines)
    {
        var file = Path.Combine(_projects, "p", "s.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, string.Join('\n', lines) + "\n");
    }

    private string Line(long input = 0, long output = 0, long cacheWrite = 0, long cacheRead = 0)
    {
        var stamp = _now.AddMinutes(-10).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);
        return "{\"type\":\"assistant\",\"timestamp\":\"" + stamp + "\",\"requestId\":\"r1\",\"sessionId\":\"s\","
            + "\"message\":{\"id\":\"m1\",\"model\":\"claude-opus-5\",\"usage\":{"
            + "\"input_tokens\":" + Number(input) + ",\"output_tokens\":" + Number(output)
            + ",\"cache_creation_input_tokens\":" + Number(cacheWrite) + ",\"cache_read_input_tokens\":" + Number(cacheRead) + "}}}";
    }
}
