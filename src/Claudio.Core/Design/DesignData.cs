using System.Reflection;
using System.Text.Json;

namespace Claudio.Core.Design;

/// <summary>Opens the design files embedded from Claudy's <c>Design/</c> folder.</summary>
internal static class DesignData
{
    public static JsonDocument Open(string name)
    {
        var assembly = typeof(DesignData).Assembly;
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing design resource {name}: run Scripts/sync-claudy.ps1.");
        return JsonDocument.Parse(stream);
    }
}
