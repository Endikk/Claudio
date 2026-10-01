using System.Globalization;
using System.Text.Json;

namespace Claudio.Core.Design;

/// <summary>An sRGB colour with its alpha.</summary>
public readonly record struct Rgba(byte R, byte G, byte B, byte A = 255)
{
    /// <summary>"#d97757" or "#ffe3b059".</summary>
    public static Rgba Parse(string hex)
    {
        var digits = hex.TrimStart('#');
        if (digits.Length is not (6 or 8))
        {
            throw new FormatException($"Not a colour: {hex}");
        }
        byte Channel(int index) => byte.Parse(digits.AsSpan(index, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgba(Channel(0), Channel(2), Channel(4), digits.Length == 8 ? Channel(6) : (byte)255);
    }

    /// <summary>This colour painted over <paramref name="below"/>, as SwiftUI stacks layers.</summary>
    public Rgba Over(Rgba below)
    {
        double alpha = A / 255.0, belowAlpha = below.A / 255.0;
        var outAlpha = alpha + belowAlpha * (1 - alpha);
        if (outAlpha <= 0)
        {
            return default;
        }
        byte Mix(byte top, byte bottom) =>
            (byte)Math.Round((top * alpha + bottom * belowAlpha * (1 - alpha)) / outAlpha);
        return new Rgba(Mix(R, below.R), Mix(G, below.G), Mix(B, below.B), (byte)Math.Round(outAlpha * 255));
    }
}

/// <summary>A spring as SwiftUI defines it.</summary>
public readonly record struct Spring(double ResponseSeconds, double DampingFraction);

/// <summary>
/// Claudy's design tokens, in the W3C Design Tokens format, read from the file Claudy exports.
/// Paths are dotted: <c>color.accent.coral</c>, <c>dimension.cardCorner</c>.
/// </summary>
public sealed class DesignTokens
{
    private readonly Dictionary<string, JsonElement> _values = new(StringComparer.Ordinal);

    private DesignTokens(JsonElement root) => Collect(root, string.Empty);

    public static DesignTokens Shared { get; } = Load();

    public static DesignTokens Load()
    {
        using var document = DesignData.Open("Design/claudy.tokens.json");
        return new DesignTokens(document.RootElement);
    }

    public IEnumerable<string> Paths => _values.Keys;

    public Rgba Color(string path) => Rgba.Parse(Value(path).GetString()!);

    /// <summary>A dimension in device-independent pixels.</summary>
    public double Dimension(string path)
    {
        var text = Value(path).GetString()!;
        return double.Parse(text.EndsWith("px", StringComparison.Ordinal) ? text[..^2] : text,
                            CultureInfo.InvariantCulture);
    }

    public double Number(string path) => Value(path).GetDouble();

    public Spring Spring(string name) =>
        new(Value($"motion.{name}.response").GetProperty("value").GetDouble(),
            Number($"motion.{name}.dampingFraction"));

    private JsonElement Value(string path) =>
        _values.TryGetValue(path, out var value) ? value : throw new KeyNotFoundException($"No design token {path}");

    private void Collect(JsonElement group, string prefix)
    {
        foreach (var property in group.EnumerateObject())
        {
            if (property.Name.StartsWith('$') || property.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }
            var path = prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}";
            if (property.Value.TryGetProperty("$value", out var value))
            {
                _values[path] = value.Clone();
            }
            else
            {
                Collect(property.Value, path);
            }
        }
    }
}
