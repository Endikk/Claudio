using Claudio.Core.Design;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

namespace Claudio.App;

/// <summary>
/// Claudy's pixel mascot, drawn cell by cell from the shared frames: one path per ink, each cell
/// a whole number of device pixels so edges stay crisp at any scale.
/// </summary>
public sealed partial class MascotView : Canvas
{
    private readonly DispatcherTimer _timer = new();
    private int _tick;

    public MascotView()
    {
        UseLayoutRounding = true;
        _timer.Tick += (_, _) => Advance();
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => _timer.Stop();
        SizeChanged += (_, _) => Draw(CurrentFrame);
    }

    public Rgba Tint { get; set; } = DesignTokens.Shared.Color("color.accent.coral");

    private bool _isTyping;

    public bool IsTyping
    {
        get => _isTyping;
        set
        {
            _isTyping = value;
            Restart();
        }
    }

    private SpriteFrame CurrentFrame =>
        _isTyping
            ? Mascot.Shared.TypingLoop[_tick % Mascot.Shared.TypingLoop.Count]
            : Mascot.Shared.Poses["resting"];

    private void Restart()
    {
        _timer.Stop();
        if (_isTyping)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(CurrentFrame.Milliseconds);
            _timer.Start();
        }
        Draw(CurrentFrame);
    }

    private void Advance()
    {
        _tick++;
        Draw(CurrentFrame);
    }

    private void Draw(SpriteFrame frame)
    {
        Children.Clear();
        if (frame.Columns == 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var scale = XamlRoot?.RasterizationScale ?? 1.0;
        var raw = Math.Min(ActualWidth / frame.Columns, ActualHeight / frame.Rows.Count);
        var cell = Math.Max(1, Math.Floor(raw * scale)) / scale;

        var geometries = new Dictionary<char, GeometryGroup>();
        for (var row = 0; row < frame.Rows.Count; row++)
        {
            var line = frame.Rows[row];
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
                    Rect = new Windows.Foundation.Rect(column * cell, row * cell, cell, cell),
                });
            }
        }

        foreach (var (ink, group) in geometries)
        {
            if (Mascot.Shared.Paint(ink, Tint, DesignTokens.Shared) is not { } colour)
            {
                continue;
            }
            Children.Add(new Path
            {
                Data = group,
                Fill = new SolidColorBrush(Windows.UI.Color.FromArgb(colour.A, colour.R, colour.G, colour.B)),
            });
        }
    }
}
