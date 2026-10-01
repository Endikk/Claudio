using Claudio.Core.Design;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Claudio.App;

/// <summary>
/// Claudy's pixel mascot, drawn cell by cell from the shared frames: one path per ink, each cell a
/// whole number of device pixels so edges stay crisp at any scale. It types while a session runs;
/// when a quota fills, the laptop explodes once and the mascot stays dead until it frees up; when a
/// new Claudio is out, it waves. The explosion needs room, so it overflows the view on every side
/// without changing its layout size.
/// </summary>
public sealed partial class MascotView : Canvas
{
    private readonly DispatcherTimer _timer = new();
    private readonly Mascot _mascot = Mascot.Shared;
    private int _tick;
    private bool _isTyping;
    private bool _isWaving;
    private bool? _isOverloaded;
    private DateTimeOffset? _overloadStart;
    private DateTimeOffset _waveStart = DateTimeOffset.UtcNow;
    private Rgba _tint = DesignTokens.Shared.Color("color.accent.coral");

    public MascotView()
    {
        UseLayoutRounding = true;
        // Decoration: assistive technologies skip it, as Claudy hides its mascot from VoiceOver.
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this, Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        _timer.Tick += (_, _) => Advance();
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => _timer.Stop();
        SizeChanged += (_, _) => Draw();
    }

    public Rgba Tint
    {
        get => _tint;
        set
        {
            if (_tint != value)
            {
                _tint = value;
                Draw();
            }
        }
    }

    public bool IsTyping
    {
        get => _isTyping;
        set
        {
            if (_isTyping != value)
            {
                _isTyping = value;
                Restart();
            }
        }
    }

    /// <summary>A quota is full. Turning true on screen plays the explosion; true from the start goes straight to the dead state, since nobody saw it happen.</summary>
    public bool IsOverloaded
    {
        get => _isOverloaded == true;
        set
        {
            if (_isOverloaded == value)
            {
                return;
            }
            var witnessed = _isOverloaded is not null && IsLoaded;
            _isOverloaded = value;
            _overloadStart = value && witnessed ? DateTimeOffset.UtcNow : null;
            Restart();
        }
    }

    public bool IsWaving
    {
        get => _isWaving;
        set
        {
            if (_isWaving != value)
            {
                _isWaving = value;
                _waveStart = DateTimeOffset.UtcNow;
                Restart();
            }
        }
    }

    private void Restart()
    {
        _timer.Stop();
        if (Motion.ReducesMotion)
        {
            // A still frame of whatever the mascot is doing: resting, dead, or arm up.
            Draw();
            return;
        }
        if (IsOverloaded)
        {
            // Fine enough for the explosion's 70 ms frames, then half of the dead loop's pace.
            _timer.Interval = TimeSpan.FromMilliseconds(_overloadStart is null ? 130 : 35);
            _timer.Start();
        }
        else if (_isWaving)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(33);
            _timer.Start();
        }
        else if (_isTyping)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(_mascot.TypingLoop[0].Milliseconds);
            _timer.Start();
        }
        Draw();
    }

    private void Advance()
    {
        _tick++;
        if (_overloadStart is { } start && DateTimeOffset.UtcNow - start > _mascot.OverloadIntro)
        {
            // Hand over to the slower dead-loop pace once the intro is over.
            _overloadStart = null;
            Restart();
            return;
        }
        Draw();
    }

    private void Draw()
    {
        Children.Clear();
        var sprite = _mascot.Poses["resting"];
        if (sprite.Columns == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }
        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        double Snap(double value) => Math.Round(value * scale) / scale;
        var raw = Math.Min(ActualWidth / sprite.Columns, ActualHeight / sprite.Rows.Count);
        var cell = Math.Max(1, Math.Floor(raw * scale)) / scale;
        var spriteX = Snap((ActualWidth - (cell * sprite.Columns)) / 2);
        var spriteY = Snap((ActualHeight - (cell * sprite.Rows.Count)) / 2);

        var still = Motion.ReducesMotion;
        if (IsOverloaded)
        {
            TimeSpan? elapsed = still ? null
                : _overloadStart is { } start ? DateTimeOffset.UtcNow - start
                : _mascot.OverloadIntro + TimeSpan.FromMilliseconds(Environment.TickCount64);
            var frame = _mascot.Overload[_mascot.OverloadFrameIndex(elapsed)];
            Paint(frame.Rows, 0, frame.Rows.Count, spriteX - (_mascot.SpriteOrigin.Column * cell),
                  spriteY - (_mascot.SpriteOrigin.Row * cell), cell);
        }
        else if (_isWaving)
        {
            var frame = still ? _mascot.WaveStill : _mascot.Wave[_mascot.WaveFrameIndex(DateTimeOffset.UtcNow - _waveStart)];
            var (first, count) = _mascot.WaveContentRows;
            var waveCell = Math.Max(1, Math.Floor(Math.Min(ActualWidth / frame.Columns, ActualHeight / count) * scale)) / scale;
            Paint(frame.Rows, first, count, Snap((ActualWidth - (waveCell * frame.Columns)) / 2),
                  Snap((ActualHeight - (waveCell * count)) / 2), waveCell);
        }
        else
        {
            var frame = _isTyping && !still ? _mascot.TypingLoop[_tick % _mascot.TypingLoop.Count] : sprite;
            Paint(frame.Rows, 0, frame.Rows.Count, spriteX, spriteY, cell);
        }
    }

    private void Paint(IReadOnlyList<string> rows, int firstRow, int rowCount, double left, double top, double cell)
    {
        var geometries = new Dictionary<char, GeometryGroup>();
        for (var row = 0; row < rowCount; row++)
        {
            var line = rows[firstRow + row];
            for (var column = 0; column < line.Length; column++)
            {
                var ink = line[column];
                if (ink == '.')
                {
                    continue;
                }
                if (!geometries.TryGetValue(ink, out var group))
                {
                    group = new GeometryGroup();
                    geometries[ink] = group;
                }
                group.Children.Add(new RectangleGeometry
                {
                    Rect = new Windows.Foundation.Rect(left + (column * cell), top + (row * cell), cell, cell),
                });
            }
        }

        foreach (var (ink, group) in geometries)
        {
            if (_mascot.Paint(ink, _tint, DesignTokens.Shared) is not { } colour)
            {
                continue;
            }
            Children.Add(new Microsoft.UI.Xaml.Shapes.Path
            {
                Data = group,
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(colour.A, colour.R, colour.G, colour.B)),
            });
        }
    }
}
