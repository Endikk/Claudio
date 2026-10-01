using System.Globalization;
using System.Text.Json;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// Claudy's transcript cases, run through Claudio's scanner. Each is a <c>projects</c> folder
/// copied to a temporary one with <c>{{RECENT}}</c> replaced by a timestamp ten minutes old.
/// </summary>
public sealed class TranscriptConformanceTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures", "transcripts");

    public static TheoryData<string> Cases()
    {
        var cases = new TheoryData<string>();
        foreach (var folder in Directory.GetDirectories(Root).Order(StringComparer.Ordinal))
        {
            cases.Add(Path.GetFileName(folder));
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Transcript(string name)
    {
        var now = DateTimeOffset.UtcNow;
        var recent = now.AddMinutes(-10).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var source = Path.Combine(Root, name, "projects");
        var projects = Path.Combine(Path.GetTempPath(), $"claudio-fixture-{Guid.NewGuid():N}");
        try
        {
            foreach (var file in Directory.EnumerateFiles(source, "*.jsonl", SearchOption.AllDirectories))
            {
                var target = Path.Combine(projects, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.WriteAllText(target, File.ReadAllText(file).Replace("{{RECENT}}", recent, StringComparison.Ordinal));
            }

            var entries = new TranscriptScanner(() => [projects], () => now).Scan();

            using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, name, "expected.json")));
            var tokens = expected.RootElement.GetProperty("tokens").EnumerateArray().Select(value => value.GetInt64());
            Assert.Equal(tokens, entries.Select(entry => entry.Tokens).Order());
        }
        finally
        {
            if (Directory.Exists(projects))
            {
                Directory.Delete(projects, recursive: true);
            }
        }
    }

    [Fact]
    public void ALineAppendedLaterCompletesTheResponse()
    {
        var projects = Path.Combine(Path.GetTempPath(), $"claudio-scan-{Guid.NewGuid():N}");
        var file = Path.Combine(projects, "p", "s.jsonl");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var stamp = now.AddMinutes(-5).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
            string Line(int output) =>
                "{\"type\":\"assistant\",\"timestamp\":\"" + stamp + "\",\"requestId\":\"r1\",\"sessionId\":\"s\"," +
                "\"message\":{\"id\":\"m1\",\"model\":\"claude-opus-5\",\"usage\":{\"output_tokens\":" +
                output.ToString(CultureInfo.InvariantCulture) + "}}}";

            File.WriteAllText(file, Line(4) + "\n");
            var scanner = new TranscriptScanner(() => [projects], () => now);
            Assert.Equal([4L], scanner.Scan().Select(entry => entry.Tokens));

            File.AppendAllText(file, Line(202) + "\n");
            Assert.Equal([202L], scanner.Scan().Select(entry => entry.Tokens));
        }
        finally
        {
            Directory.Delete(projects, recursive: true);
        }
    }

    [Theory]
    [InlineData("2026-09-24T07:05:12.345Z")]
    [InlineData("2026-09-24T07:05:12.3456789123Z")]
    [InlineData("2026-09-24T09:05:12.345+02:00")]
    public void TimestampsReadToTheSameInstant(string stamp)
    {
        var date = TranscriptScanner.Timestamp(stamp)!.Value;
        var whole = date.AddTicks(-(date.Ticks % TimeSpan.TicksPerSecond));
        Assert.Equal(new DateTimeOffset(2026, 9, 24, 7, 5, 12, TimeSpan.Zero), whole);
    }
}
