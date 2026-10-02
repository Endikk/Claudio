using Claudio.Core.Services;

namespace Claudio.Core.Design;

/// <summary>One step of an outline, from where the previous one ended.</summary>
public abstract record OutlineSegment(double X, double Y);

public sealed record OutlineLine(double X, double Y) : OutlineSegment(X, Y);

/// <summary>A quadratic curve through the control point (<paramref name="ControlX"/>, <paramref name="ControlY"/>).</summary>
public sealed record OutlineCurve(double ControlX, double ControlY, double X, double Y) : OutlineSegment(X, Y);

/// <summary>A clockwise quarter circle of <paramref name="Radius"/>.</summary>
public sealed record OutlineArc(double Radius, double X, double Y) : OutlineSegment(X, Y);

/// <summary>
/// The island's outline, as Claudy's <c>NotchShape</c>: flush with the screen's top edge, which it
/// meets through a concave shoulder on each side, as a notch does; rounded at the bottom. The bottom
/// radius moves between the ears and the open island. The body sits <c>shoulder</c> inside the
/// rectangle on each side.
/// </summary>
public static class NotchShape
{
    /// <summary>The closed outline in <paramref name="rect"/>, from its top-left corner, clockwise.</summary>
    public static (double StartX, double StartY, IReadOnlyList<OutlineSegment> Segments) Path(ScreenRect rect, double bottomRadius, double shoulder = 0)
    {
        var flare = Math.Max(0, Math.Min(shoulder, Math.Min(rect.Width / 4, rect.Height / 2)));
        var left = rect.X + flare;
        var right = rect.Right - flare;
        var radius = Math.Max(0, Math.Min(bottomRadius, Math.Min(rect.Height / 2, (right - left) / 2)));
        var top = rect.Y;
        var bottom = rect.Bottom;
        var segments = new List<OutlineSegment> { new OutlineLine(rect.Right, top) };
        if (flare > 0)
        {
            segments.Add(new OutlineCurve(right, top, right, top + flare));
        }
        segments.Add(new OutlineLine(right, bottom - radius));
        segments.Add(new OutlineArc(radius, right - radius, bottom));
        segments.Add(new OutlineLine(left + radius, bottom));
        segments.Add(new OutlineArc(radius, left, bottom - radius));
        segments.Add(new OutlineLine(left, top + flare));
        if (flare > 0)
        {
            segments.Add(new OutlineCurve(left, top, rect.X, top));
        }
        return (rect.X, top, segments);
    }

    /// <summary>Whether the outline holds a point: the glass's own area, without the shoulders' hollows.</summary>
    public static bool Contains(ScreenRect rect, double bottomRadius, double shoulder, double x, double y)
    {
        var points = Flatten(Path(rect, bottomRadius, shoulder));
        var inside = false;
        for (int i = 0, j = points.Count - 1; i < points.Count; j = i++)
        {
            var (xi, yi) = points[i];
            var (xj, yj) = points[j];
            if ((yi > y) != (yj > y) && x < ((xj - xi) * (y - yi) / (yj - yi)) + xi)
            {
                inside = !inside;
            }
        }
        return inside;
    }

    private static List<(double X, double Y)> Flatten((double StartX, double StartY, IReadOnlyList<OutlineSegment> Segments) path)
    {
        const int Steps = 24;
        var points = new List<(double X, double Y)> { (path.StartX, path.StartY) };
        foreach (var segment in path.Segments)
        {
            var (fromX, fromY) = points[^1];
            switch (segment)
            {
                case OutlineCurve curve:
                    for (var step = 1; step <= Steps; step++)
                    {
                        var t = step / (double)Steps;
                        var u = 1 - t;
                        points.Add(((u * u * fromX) + (2 * u * t * curve.ControlX) + (t * t * curve.X),
                                    (u * u * fromY) + (2 * u * t * curve.ControlY) + (t * t * curve.Y)));
                    }
                    break;
                case OutlineArc arc:
                    // A quarter turn clockwise (y downwards): the end is the start turned a quarter
                    // about the centre, which places the centre.
                    var (dx, dy) = (arc.X - fromX, arc.Y - fromY);
                    var (centreX, centreY) = (fromX - ((dy - dx) / 2), fromY + ((dx + dy) / 2));
                    var start = Math.Atan2(fromY - centreY, fromX - centreX);
                    for (var step = 1; step <= Steps; step++)
                    {
                        var angle = start + (Math.PI / 2 * step / Steps);
                        points.Add((centreX + (arc.Radius * Math.Cos(angle)), centreY + (arc.Radius * Math.Sin(angle))));
                    }
                    break;
                default:
                    points.Add((segment.X, segment.Y));
                    break;
            }
        }
        return points;
    }
}
