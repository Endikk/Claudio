using System.Text.Json;

namespace Claudio.Core.Design;

/// <summary>One frame of pixel art: rows of characters, one per pixel, '.' for empty.</summary>
public sealed record SpriteFrame(string Name, IReadOnlyList<string> Rows, int Milliseconds)
{
    public int Columns => Rows.Count == 0 ? 0 : Rows[0].Length;
}

/// <summary>
/// Claudy's pixel mascot, exactly as Claudy draws it: the three typing poses, the explosion when
/// a quota fills, and the wave when a new version is out. The body takes the tint of the leading
/// gauge; every other ink is a token.
/// </summary>
public sealed class Mascot
{
    private readonly Dictionary<char, string[]> _inks;

    private Mascot(Dictionary<char, string[]> inks, IReadOnlyList<SpriteFrame> typingLoop,
                   IReadOnlyDictionary<string, SpriteFrame> poses, IReadOnlyList<SpriteFrame> overload,
                   int deadLoopCount, (int Column, int Row) spriteOrigin, IReadOnlyList<SpriteFrame> wave)
    {
        _inks = inks;
        TypingLoop = typingLoop;
        Poses = poses;
        Overload = overload;
        DeadLoopCount = deadLoopCount;
        SpriteOrigin = spriteOrigin;
        Wave = wave;
    }

    public static Mascot Shared { get; } = Load();

    /// <summary>Resting, left hand down, right hand down.</summary>
    public IReadOnlyDictionary<string, SpriteFrame> Poses { get; }

    /// <summary>The typing loop in playback order.</summary>
    public IReadOnlyList<SpriteFrame> TypingLoop { get; }

    /// <summary>The explosion, then the dead state: its last <see cref="DeadLoopCount"/> frames loop.</summary>
    public IReadOnlyList<SpriteFrame> Overload { get; }

    public int DeadLoopCount { get; }

    /// <summary>Where the typing sprite sits inside the larger explosion grid.</summary>
    public (int Column, int Row) SpriteOrigin { get; }

    /// <summary>The wave in playback order.</summary>
    public IReadOnlyList<SpriteFrame> Wave { get; }

    public IEnumerable<char> Inks => _inks.Keys;

    /// <summary>Length of the explosion's one-off part, before the dead loop starts.</summary>
    public TimeSpan OverloadIntro =>
        TimeSpan.FromMilliseconds(Overload.Take(Overload.Count - DeadLoopCount).Sum(frame => frame.Milliseconds));

    /// <summary>
    /// The explosion frame to show <paramref name="elapsed"/> after the quota filled up. Null means
    /// the explosion was not witnessed (Claudio opened already full): straight to the dead loop.
    /// </summary>
    public int OverloadFrameIndex(TimeSpan? elapsed)
    {
        var deadStart = Overload.Count - DeadLoopCount;
        if (elapsed is not { } time)
        {
            return deadStart;
        }
        var remaining = Math.Max(time.TotalMilliseconds, 0);
        for (var index = 0; index < deadStart; index++)
        {
            if (remaining < Overload[index].Milliseconds)
            {
                return index;
            }
            remaining -= Overload[index].Milliseconds;
        }
        var loop = Math.Max(Overload.Skip(deadStart).Sum(frame => frame.Milliseconds), 1);
        var inLoop = (long)remaining % loop;
        for (var index = deadStart; index < Overload.Count; index++)
        {
            if (inLoop < Overload[index].Milliseconds)
            {
                return index;
            }
            inLoop -= Overload[index].Milliseconds;
        }
        return Overload.Count - 1;
    }

    /// <summary>Arm up and smiling: the wave's still frame when Windows' animations are off, as Claudy's under "Reduce motion".</summary>
    public SpriteFrame WaveStill => Wave.FirstOrDefault(frame => frame.Name == "04-wave-up") ?? Wave[0];

    /// <summary>The wave frame on screen <paramref name="elapsed"/> into its loop.</summary>
    public int WaveFrameIndex(TimeSpan elapsed)
    {
        var loop = Math.Max(Wave.Sum(frame => frame.Milliseconds), 1);
        var remaining = (long)elapsed.TotalMilliseconds % loop;
        for (var index = 0; index < Wave.Count; index++)
        {
            if (remaining < Wave[index].Milliseconds)
            {
                return index;
            }
            remaining -= Wave[index].Milliseconds;
        }
        return 0;
    }

    /// <summary>
    /// Rows any wave frame draws on. The grid keeps empty rows for the export's margin; cropping
    /// them lets the waving mascot take the typing one's place at the same scale.
    /// </summary>
    public (int First, int Count) WaveContentRows
    {
        get
        {
            var used = Wave.SelectMany(frame => Enumerable.Range(0, frame.Rows.Count).Where(row => frame.Rows[row].Any(ink => ink != '.'))).ToList();
            return used.Count == 0 ? (0, 0) : (used.Min(), used.Max() - used.Min() + 1);
        }
    }

    /// <summary>
    /// The colours stacked on one ink, bottom first: the tint for the body, tokens for the rest.
    /// An unknown ink, or '.', draws nothing.
    /// </summary>
    public IReadOnlyList<Rgba> Layers(char ink, Rgba tint, DesignTokens tokens) =>
        _inks.TryGetValue(ink, out var layers)
            ? layers.Select(layer => layer == "tint" ? tint : tokens.Color(layer)).ToList()
            : [];

    /// <summary>The single colour an ink resolves to, its layers flattened.</summary>
    public Rgba? Paint(char ink, Rgba tint, DesignTokens tokens)
    {
        var layers = Layers(ink, tint, tokens);
        if (layers.Count == 0)
        {
            return null;
        }
        var colour = layers[0];
        for (var index = 1; index < layers.Count; index++)
        {
            colour = layers[index].Over(colour);
        }
        return colour;
    }

    public static Mascot Load()
    {
        using var palette = DesignData.Open("Design/mascot/palette.json");
        var inks = palette.RootElement.GetProperty("inks").EnumerateObject().ToDictionary(
            ink => ink.Name[0],
            ink => ink.Value.EnumerateArray().Select(layer => layer.GetString()!).ToArray());

        using var typing = DesignData.Open("Design/mascot/typing.json");
        var frameMs = typing.RootElement.GetProperty("frameMs").GetInt32();
        var poses = typing.RootElement.GetProperty("poses").EnumerateObject().ToDictionary(
            pose => pose.Name, pose => new SpriteFrame(pose.Name, Rows(pose.Value), frameMs));
        var loop = typing.RootElement.GetProperty("sequence").EnumerateArray()
                         .Select(step => poses[step.GetString()!]).ToList();

        using var overload = DesignData.Open("Design/mascot/overload.json");
        var durations = overload.RootElement.GetProperty("frameMs").EnumerateArray().Select(ms => ms.GetInt32()).ToList();
        var explosion = overload.RootElement.GetProperty("frames").EnumerateArray()
                                .Select((frame, index) => new SpriteFrame($"overload-{index:00}", Rows(frame), durations[index]))
                                .ToList();
        var origin = overload.RootElement.GetProperty("spriteOrigin");

        using var wave = DesignData.Open("Design/mascot/wave.json");
        var waveFrames = wave.RootElement.GetProperty("frames").EnumerateArray().ToList();
        var waveLoop = wave.RootElement.GetProperty("sequence").EnumerateArray().Select(step =>
        {
            var frame = waveFrames[step.GetProperty("frame").GetInt32()];
            return new SpriteFrame(frame.GetProperty("name").GetString()!, Rows(frame.GetProperty("rows")),
                                   step.GetProperty("ms").GetInt32());
        }).ToList();

        return new Mascot(inks, loop, poses, explosion,
                          overload.RootElement.GetProperty("deadLoopCount").GetInt32(),
                          (origin.GetProperty("column").GetInt32(), origin.GetProperty("row").GetInt32()),
                          waveLoop);
    }

    private static List<string> Rows(JsonElement rows) =>
        rows.EnumerateArray().Select(row => row.GetString()!).ToList();
}
