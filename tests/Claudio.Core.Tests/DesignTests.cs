using Claudio.Core.Design;

namespace Claudio.Core.Tests;

/// <summary>Claudio wears Claudy's look: same palette, same metrics, same mascot.</summary>
public sealed class DesignTests
{
    private readonly DesignTokens _tokens = DesignTokens.Shared;
    private readonly Mascot _mascot = Mascot.Shared;

    [Fact]
    public void AccentsAreClaudys()
    {
        Assert.Equal(new Rgba(0xD9, 0x77, 0x57), _tokens.Color("color.accent.coral"));
        Assert.Equal(new Rgba(0xF0, 0x60, 0x4D), _tokens.Color("color.danger"));
        Assert.Equal(new Rgba(0x3E, 0xC6, 0xBC), _tokens.Color("color.accent.teal"));
        Assert.Equal(new Rgba(0xFF, 0xE3, 0xB0, 0x59), _tokens.Color("color.pixel.highlight"));
    }

    [Fact]
    public void MetricsAndMotionAreRead()
    {
        Assert.Equal(28, _tokens.Dimension("dimension.cardCorner"));
        Assert.Equal(18, _tokens.Dimension("dimension.tileCorner"));
        Assert.Equal(340, _tokens.Dimension("dimension.fullWidth"));
        Assert.Equal(28, _tokens.Dimension("dimension.shadowInset"));
        Assert.Equal(new Spring(0.55, 0.95), _tokens.Spring("gauge"));
        Assert.Equal(0.95, _tokens.Number("threshold.strain"));
    }

    [Fact]
    public void TypingLoopAlternatesHandsThenRests()
    {
        Assert.Equal(["leftDown", "rightDown", "leftDown", "rightDown", "resting", "resting"],
                     _mascot.TypingLoop.Select(frame => frame.Name));
        Assert.All(_mascot.Poses.Values, pose =>
        {
            Assert.Equal(27, pose.Rows.Count);
            Assert.All(pose.Rows, row => Assert.Equal(29, row.Length));
            Assert.Equal(160, pose.Milliseconds);
        });
    }

    [Fact]
    public void FramesAreRectangular()
    {
        foreach (var frame in _mascot.Overload.Concat(_mascot.Wave))
        {
            Assert.All(frame.Rows, row => Assert.Equal(frame.Columns, row.Length));
        }
        Assert.Equal(16, _mascot.Overload.Count);
        Assert.InRange(_mascot.DeadLoopCount, 1, _mascot.Overload.Count);
    }

    [Fact]
    public void EveryInkUsedIsPainted()
    {
        var used = _mascot.Poses.Values.Concat(_mascot.Overload).Concat(_mascot.Wave)
                          .SelectMany(frame => frame.Rows).SelectMany(row => row)
                          .Where(ink => ink != '.').ToHashSet();
        var coral = _tokens.Color("color.accent.coral");
        Assert.All(used, ink => Assert.NotNull(_mascot.Paint(ink, coral, _tokens)));
    }

    /// <summary>Claudy's own values: 0.7 s left at 0.5 s into the loop, 0.09 s at 1.2 s.</summary>
    [Fact]
    public void TheWaveWaitsAsClaudysDoes()
    {
        Assert.Equal(0.7, _mascot.TimeToNextWaveFrame(TimeSpan.FromSeconds(0.5)).TotalSeconds, 3);
        Assert.Equal(0.09, _mascot.TimeToNextWaveFrame(TimeSpan.FromSeconds(1.2)).TotalSeconds, 3);
    }

    /// <summary>The wave is woken when its frame changes: the wait always lands on the next frame.</summary>
    [Fact]
    public void TheWaveWakesWhenItsFrameChanges()
    {
        var first = _mascot.Wave[0].Milliseconds;
        Assert.Equal(TimeSpan.FromMilliseconds(first), _mascot.TimeToNextWaveFrame(TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMilliseconds(first - 10), _mascot.TimeToNextWaveFrame(TimeSpan.FromMilliseconds(10)));
        var loop = _mascot.Wave.Sum(frame => frame.Milliseconds);
        for (var at = 0; at < loop; at += 37)
        {
            var elapsed = TimeSpan.FromMilliseconds(at);
            var next = elapsed + _mascot.TimeToNextWaveFrame(elapsed) + TimeSpan.FromMilliseconds(2);
            Assert.NotEqual(_mascot.WaveFrameIndex(elapsed), _mascot.WaveFrameIndex(next));
        }
    }

    [Fact]
    public void BodyTakesTheTintAndShadesIt()
    {
        var coral = _tokens.Color("color.accent.coral");
        Assert.Equal(coral, _mascot.Paint('C', coral, _tokens));
        var shaded = _mascot.Paint('D', coral, _tokens)!.Value;
        Assert.True(shaded.R < coral.R && shaded.A == 255);
        Assert.Null(_mascot.Paint('.', coral, _tokens));
    }
}
