using Claudio.Core.Services;

namespace Claudio.Core.Tests;

public sealed class ProcessEnvironmentTests
{
    [Fact]
    public void DetectsClaudeCodeMarker() =>
        Assert.Equal(new ProcessEnvironment.Markers(true, null),
                     ProcessEnvironment.Parse([@"PATH=C:\Windows", "CLAUDECODE=1", "TERM=xterm"]));

    [Fact]
    public void ReadsProjectDirectory()
    {
        var markers = ProcessEnvironment.Parse([@"CLAUDE_PROJECT_DIR=C:\Users\x\Dev\Surikat"]);
        Assert.True(markers.IsClaude);
        Assert.Equal(@"C:\Users\x\Dev\Surikat", markers.ProjectDirectory);
    }

    [Fact]
    public void EntrypointAloneIsEnough() =>
        Assert.True(ProcessEnvironment.Parse(["CLAUDE_CODE_ENTRYPOINT=claude-vscode"]).IsClaude);

    [Fact]
    public void UnrelatedProcessHasNoMarkers() =>
        Assert.Equal(new ProcessEnvironment.Markers(false, null),
                     ProcessEnvironment.Parse([@"PATH=C:\Windows", @"USERPROFILE=C:\Users\x", "CLAUDE_CONFIG_DIR=x"]));

    /// <summary>Windows reads <c>ClaudeCode</c> and <c>CLAUDECODE</c> as the same variable.</summary>
    [Fact]
    public void NamesAreCaseInsensitive()
    {
        Assert.True(ProcessEnvironment.Parse(["ClaudeCode=1"]).IsClaude);
        Assert.Equal(@"C:\Dev\App", ProcessEnvironment.Parse([@"claude_project_dir=C:\Dev\App"]).ProjectDirectory);
    }

    /// <summary>
    /// The decisive case: a value that merely mentions a marker does not attribute the process,
    /// nor does the shell's per-drive directory entry, which opens with <c>=</c>.
    /// </summary>
    [Fact]
    public void AMentionIsNotAnAttribution() =>
        Assert.False(ProcessEnvironment.Parse([@"=C:=C:\CLAUDECODE=1", "ARGS=echo CLAUDECODE=1", "NOTCLAUDECODE=1", "CLAUDECODE"]).IsClaude);

    [Fact]
    public void ReadsARawBlock()
    {
        var block = "=C:=C:\\Dev\0PATH=C:\\Windows\0CLAUDECODE=1\0CLAUDE_PROJECT_DIR=C:\\Dev\\Surikat\0\0";
        Assert.Equal(new ProcessEnvironment.Markers(true, @"C:\Dev\Surikat"), ProcessEnvironment.ParseBlock(block));
    }

    /// <summary>The block ends at its empty entry: what lies past it is not the environment.</summary>
    [Fact]
    public void StopsAtTheEndOfTheBlock() =>
        Assert.False(ProcessEnvironment.ParseBlock("PATH=C:\\Windows\0\0CLAUDECODE=1\0\0").IsClaude);

    /// <summary>A read that came back short ends mid-entry: that entry is dropped, never half read.</summary>
    [Fact]
    public void ACutEntryIsDropped() =>
        Assert.Equal(new ProcessEnvironment.Markers(true, null),
                     ProcessEnvironment.ParseBlock("CLAUDECODE=1\0CLAUDE_PROJECT_DIR=C:\\Us"));

    [Fact]
    public void AnEmptyBlockHasNoMarkers() =>
        Assert.Equal(new ProcessEnvironment.Markers(false, null), ProcessEnvironment.ParseBlock(""));
}
