using Claudio.Core.Services;

namespace Claudio.Core.Tests;

/// <summary>
/// The settings file can never keep Claudio from starting, and a crash can never leave it half
/// written: whatever the file holds, a read answers; a write replaces it whole.
/// </summary>
public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "claudio-settings-" + Guid.NewGuid());
    private readonly List<string> _log = [];

    private string File_ => Path.Combine(_folder, "settings.json");

    private SettingsStore Open() => new(File_, _log.Add);

    private void Write(string text)
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(File_, text);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void AMissingFileReadsAsTheDefaults()
    {
        var store = Open();

        Assert.True(store.Bool("alwaysOnTop", true));
        Assert.False(store.Bool("isMinimal", false));
        Assert.Null(store.Text("placement"));
        Assert.Empty(_log);
    }

    [Fact]
    public void WhatWasSetIsKeptAcrossRuns()
    {
        var store = Open();
        store.Set("placement", "Notch");
        store.Set("isMinimal", true);

        var next = Open();

        Assert.Equal("Notch", next.Text("placement"));
        Assert.True(next.Bool("isMinimal", false));
    }

    [Fact]
    public void ASettingIsNotLostByAnotherOne()
    {
        var store = Open();
        store.Set("a", true);
        store.Set("b", "x");
        store.Set("a", false);

        var next = Open();

        Assert.False(next.Bool("a", true));
        Assert.Equal("x", next.Text("b"));
    }

    [Fact]
    public void NullRemovesTheKey()
    {
        var store = Open();
        store.Set("update.announced", "1.0.1");
        store.Set("update.announced", null);

        Assert.Null(Open().Text("update.announced"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{\"placement\": ")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"text\"")]
    [InlineData("null")]
    public void ADamagedFileReadsAsTheDefaultsAndIsKept(string damaged)
    {
        Write(damaged);

        var store = Open();

        Assert.True(store.Bool("alwaysOnTop", true));
        Assert.Null(store.Text("placement"));
        if (damaged.Length > 0 && damaged != "null")
        {
            Assert.Equal(damaged, File.ReadAllText(File_ + ".bad"));
            Assert.Contains(_log, line => line.Contains("damaged", StringComparison.Ordinal));
        }
    }

    [Fact]
    public void ADamagedFileIsReplacedByTheFirstSaveAndStaysOnBeside()
    {
        Write("{broken");
        var store = Open();

        store.Set("isMinimal", true);

        Assert.True(Open().Bool("isMinimal", false));
        Assert.Equal("{broken", File.ReadAllText(File_ + ".bad"));
    }

    /// <summary>A hand-edited number where text goes, or text where a flag goes, must not throw.</summary>
    [Fact]
    public void AKeyOfTheWrongTypeReadsAsTheDefault()
    {
        Write("""{"placement": 5, "isMinimal": "yes", "alwaysOnTop": null, "configDir": {"a": 1}, "update.announced": [1]}""");

        var store = Open();

        Assert.Null(store.Text("placement"));
        Assert.False(store.Bool("isMinimal", false));
        Assert.True(store.Bool("alwaysOnTop", true));
        Assert.Null(store.Text("configDir"));
        Assert.Null(store.Text("update.announced"));
    }

    [Fact]
    public void ARunNeverLeavesATemporaryFileBehind()
    {
        var store = Open();
        for (var index = 0; index < 20; index++)
        {
            store.Set("n", index % 2 == 0);
        }

        Assert.Equal([File_], Directory.GetFiles(_folder));
    }

    /// <summary>The previous file is still whole when the write of the next one dies halfway.</summary>
    [Fact]
    public void ALeftOverTemporaryFileDoesNotHarmTheNextSave()
    {
        var store = Open();
        store.Set("isMinimal", true);
        File.WriteAllText(File_ + ".tmp", "{half a fi");

        store.Set("isMinimal", false);

        Assert.False(Open().Bool("isMinimal", true));
        Assert.False(File.Exists(File_ + ".tmp"));
    }

    [Fact]
    public void AFolderThatCannotBeWrittenDoesNotThrow()
    {
        // A file where the folder should be: the save cannot happen, and Claudio carries on.
        var blocker = Path.Combine(Path.GetTempPath(), "claudio-blocker-" + Guid.NewGuid());
        File.WriteAllText(blocker, "x");
        try
        {
            var store = new SettingsStore(Path.Combine(blocker, "settings.json"), _log.Add);

            store.Set("isMinimal", true);

            Assert.True(store.Bool("isMinimal", false));
            Assert.Contains(_log, line => line.Contains("not saved", StringComparison.Ordinal));
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public async Task ManyWritersLeaveAWholeFile()
    {
        var store = Open();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(worker => Task.Run(() =>
        {
            for (var index = 0; index < 25; index++)
            {
                store.Set($"key{worker}", index % 2 == 0);
                store.Set("shared", worker % 2 == 0);
            }
        }, TestContext.Current.CancellationToken)));

        var next = Open();
        for (var worker = 0; worker < 8; worker++)
        {
            // The last write of each worker, index 24, is even.
            Assert.True(next.Bool($"key{worker}", false));
        }
        Assert.DoesNotContain(_log, line => line.Contains("damaged", StringComparison.Ordinal));
    }
}
