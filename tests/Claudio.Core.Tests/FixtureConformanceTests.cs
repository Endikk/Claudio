using System.Text.Json;
using Claudio.Core.Models;
using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// The cases Claudy writes down in <c>Fixtures/</c>, run against Claudio's parsers. Claudy runs
/// the very same files against its own: when a fix lands there with a new case, this test fails
/// here until Claudio reads the answer the same way.
/// </summary>
public sealed class FixtureConformanceTests
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "Fixtures");

    public static TheoryData<string> UsageCases()
    {
        var cases = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Root, "usage"), "*.expected.json").Order(StringComparer.Ordinal))
        {
            cases.Add(Path.GetFileName(file).Replace(".expected.json", string.Empty, StringComparison.Ordinal));
        }
        return cases;
    }

    [Theory]
    [MemberData(nameof(UsageCases))]
    public void UsageAnswer(string name)
    {
        var folder = Path.Combine(Root, "usage");
        using var expected = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, $"{name}.expected.json")));
        var now = DateTimeOffset.Parse(expected.RootElement.GetProperty("now").GetString()!, System.Globalization.CultureInfo.InvariantCulture);

        var reading = UsageParser.Parse(File.ReadAllText(Path.Combine(folder, $"{name}.json")), now);

        var wanted = expected.RootElement.GetProperty("reading");
        if (wanted.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(reading);
            return;
        }
        Assert.NotNull(reading);
        Assert.Equal(QuotaSourceKind.Api, reading.Source.Kind);
        CheckWindow(reading.Session, wanted.GetProperty("session"));
        CheckWindow(reading.Weekly, wanted.GetProperty("weekly"));
        CheckWindow(reading.Scoped, wanted.GetProperty("scoped"));
        CheckSpend(reading.Spend, wanted.GetProperty("spend"));
    }

    [Fact]
    public void PlanLabels()
    {
        foreach (var item in Cases("plan-labels.json"))
        {
            var candidates = item.GetProperty("candidates").EnumerateArray()
                                 .Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() : null)
                                 .ToList();
            Assert.Equal(item.GetProperty("label").GetString(), PlanLabel.From(candidates));
        }
    }

    [Fact]
    public void ModelNames()
    {
        foreach (var item in Cases("model-names.json"))
        {
            var identifier = item.GetProperty("identifier").GetString()!;
            Assert.Equal(item.GetProperty("display").GetString(), ModelName.Display(identifier));
            Assert.Equal(item.GetProperty("accent").GetString(), ModelName.AccentOf(identifier).ToString().ToLowerInvariant());
        }
    }

    private static List<JsonElement> Cases(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, file)));
        return document.RootElement.GetProperty("cases").EnumerateArray().Select(item => item.Clone()).ToList();
    }

    private static DateTimeOffset? Date(JsonElement value) =>
        value.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(value.GetString()!, System.Globalization.CultureInfo.InvariantCulture)
            : null;

    private static void CheckWindow(QuotaWindow? window, JsonElement wanted)
    {
        if (wanted.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(window);
            return;
        }
        Assert.NotNull(window);
        Assert.Equal(wanted.GetProperty("percent").GetDouble(), window.Percent, 4);
        Assert.Equal(Date(wanted.GetProperty("resetsAt")), window.ResetsAt);
        Assert.Equal(wanted.TryGetProperty("label", out var label) ? label.GetString() : null, window.Label);
    }

    private static void CheckSpend(SpendReading? spend, JsonElement wanted)
    {
        if (wanted.ValueKind == JsonValueKind.Null)
        {
            Assert.Null(spend);
            return;
        }
        Assert.NotNull(spend);
        Assert.Equal(wanted.GetProperty("used").GetDouble(), spend.Used, 4);
        Assert.Equal(wanted.GetProperty("limit").GetDouble(), spend.Limit, 4);
        Assert.Equal(wanted.GetProperty("percent").GetDouble(), spend.Percent, 5);
        Assert.Equal(wanted.GetProperty("currency").GetString(), spend.Currency);
        Assert.Equal(wanted.GetProperty("isLimitReached").GetBoolean(), spend.IsLimitReached);
        Assert.Equal(Date(wanted.GetProperty("resetsAt")), spend.ResetsAt);
    }
}
