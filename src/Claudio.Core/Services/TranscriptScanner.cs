using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Claudio.Core.Services;

/// <summary>One model response recorded in a transcript.</summary>
public sealed record TranscriptEntry(
    DateTimeOffset Date,
    string Model,
    long Tokens,
    string Cwd,
    string SessionId,
    bool IsSidechain,
    string? DedupKey);

/// <summary>
/// Reads Claude Code's <c>projects/**/*.jsonl</c> transcripts, as Claudy's <c>TranscriptScanner</c>
/// does. Transcripts only ever grow, so a cursor per file means each pass reads only what was
/// appended since the last one. Claude Code writes one response over several lines and copies
/// responses into resumed sessions: every response counts once, at its largest copy.
/// </summary>
public sealed class TranscriptScanner
{
    /// <summary>The widest view is seven days; the margin absorbs time zones.</summary>
    private static readonly TimeSpan Retention = TimeSpan.FromDays(9);
    private static readonly byte[] UsageMarker = "\"usage\""u8.ToArray();

    private readonly Func<IReadOnlyList<string>> _projectsDirectories;
    private readonly Func<DateTimeOffset> _now;
    private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);

    public TranscriptScanner(Func<IReadOnlyList<string>> projectsDirectories, Func<DateTimeOffset>? now = null)
    {
        _projectsDirectories = projectsDirectories;
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    private sealed class FileState
    {
        public long Offset;
        public DateTime Created;
        public Dictionary<string, TranscriptEntry> Responses = new(StringComparer.Ordinal);
        public DateTimeOffset LastSeen;
    }

    /// <summary>Every response over the retention window, oldest first.</summary>
    public IReadOnlyList<TranscriptEntry> Scan()
    {
        var now = _now();
        var cutoff = now - Retention;

        var discovered = TranscriptFiles(cutoff);
        foreach (var file in discovered)
        {
            Ingest(file, cutoff, now);
        }

        var seen = discovered.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, state) in _files.ToList())
        {
            if (!seen.Contains(path) && state.LastSeen < cutoff)
            {
                _files.Remove(path);
            }
            else
            {
                state.Responses = state.Responses.Where(pair => pair.Value.Date >= cutoff)
                                                 .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            }
        }

        // Settled across files rather than while reading, so the result never depends on the
        // order the disk lists files in.
        var unique = new Dictionary<string, TranscriptEntry>(StringComparer.Ordinal);
        foreach (var state in _files.Values)
        {
            foreach (var (key, entry) in state.Responses)
            {
                if (!unique.TryGetValue(key, out var held) || IsMoreComplete(entry, held))
                {
                    unique[key] = entry;
                }
            }
        }
        return unique.Values.OrderBy(entry => entry.Date).ToList();
    }

    /// <summary>Counters only grow, so the largest copy is the final one; ties go to the earliest.</summary>
    private static bool IsMoreComplete(TranscriptEntry candidate, TranscriptEntry held)
    {
        if (candidate.Tokens != held.Tokens)
        {
            return candidate.Tokens > held.Tokens;
        }
        if (candidate.Date != held.Date)
        {
            return candidate.Date < held.Date;
        }
        return string.CompareOrdinal(candidate.SessionId, held.SessionId) < 0;
    }

    private List<string> TranscriptFiles(DateTimeOffset cutoff)
    {
        var found = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in _projectsDirectories())
        {
            if (!Directory.Exists(folder) || !visited.Add(Path.GetFullPath(folder)))
            {
                continue;
            }
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            foreach (var file in Directory.EnumerateFiles(folder, "*.jsonl", options))
            {
                if (File.GetLastWriteTimeUtc(file) >= cutoff.UtcDateTime)
                {
                    found.Add(file);
                }
            }
        }
        return found;
    }

    private void Ingest(string path, DateTimeOffset cutoff, DateTimeOffset now)
    {
        DateTime created;
        byte[] data;
        long size;
        try
        {
            created = File.GetCreationTimeUtc(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            size = stream.Length;
            if (!_files.TryGetValue(path, out var known))
            {
                known = new FileState { Created = created };
                _files[path] = known;
            }
            known.LastSeen = now;
            // Replaced or truncated: start over, the responses it held go with it.
            if (known.Created != created || size < known.Offset)
            {
                known.Offset = 0;
                known.Responses.Clear();
                known.Created = created;
            }
            if (size <= known.Offset)
            {
                return;
            }
            stream.Seek(known.Offset, SeekOrigin.Begin);
            data = new byte[size - known.Offset];
            stream.ReadExactly(data);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        var state = _files[path];
        var start = state.Offset;
        var (lines, consumed) = UsageLines(data);
        state.Offset += consumed;
        var name = Path.GetFileName(path);

        foreach (var (lineStart, length) in lines)
        {
            if (Parse(data.AsSpan(lineStart, length)) is not { } entry || entry.Date < cutoff)
            {
                continue;
            }
            var key = entry.DedupKey ?? $"{name}@{start + lineStart}";
            if (!state.Responses.TryGetValue(key, out var held) || IsMoreComplete(entry, held))
            {
                state.Responses[key] = entry;
            }
        }
    }

    /// <summary>
    /// The lines holding a usage block, and how many bytes were consumed. A tail without its
    /// newline is a write in progress, left for the next pass, unless it parses whole.
    /// </summary>
    private static (List<(int Start, int Length)> Lines, long Consumed) UsageLines(byte[] data)
    {
        var lines = new List<(int, int)>();
        var span = data.AsSpan();
        var lineStart = 0;
        while (lineStart < span.Length)
        {
            var newline = span[lineStart..].IndexOf((byte)'\n');
            if (newline < 0)
            {
                break;
            }
            if (span.Slice(lineStart, newline).IndexOf(UsageMarker) >= 0)
            {
                lines.Add((lineStart, newline));
            }
            lineStart += newline + 1;
        }
        if (lineStart >= span.Length)
        {
            return (lines, span.Length);
        }

        var tail = span[lineStart..];
        try
        {
            using var document = JsonDocument.Parse(tail.ToArray());
        }
        catch (JsonException)
        {
            return (lines, lineStart);
        }
        if (tail.IndexOf(UsageMarker) >= 0)
        {
            lines.Add((lineStart, tail.Length));
        }
        return (lines, span.Length);
    }

    private static TranscriptEntry? Parse(ReadOnlySpan<byte> line)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || Text(root, "type") != "assistant"
                || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object
                || Text(message, "model") is not { } model || model.StartsWith('<')
                || Timestamp(Text(root, "timestamp")) is not { } date)
            {
                return null;
            }

            var tokens = Count(usage, "input_tokens") + Count(usage, "output_tokens")
                + Count(usage, "cache_creation_input_tokens") + Count(usage, "cache_read_input_tokens");
            if (tokens <= 0)
            {
                return null;
            }

            var messageId = Text(message, "id");
            var requestId = Text(root, "requestId");
            var key = !string.IsNullOrEmpty(messageId) && !string.IsNullOrEmpty(requestId) ? $"{messageId}:{requestId}" : null;
            var sidechain = root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True;
            return new TranscriptEntry(date, model, tokens, Text(root, "cwd") ?? string.Empty,
                                       Text(root, "sessionId") ?? string.Empty, sidechain, key);
        }
    }

    /// <summary>
    /// <c>2026-09-24T07:05:12.345Z</c>. A fraction longer than .NET reads (seven digits) is cut,
    /// as Claudy cuts it at nine: no transcript needs finer than that.
    /// </summary>
    public static DateTimeOffset? Timestamp(string? stamp)
    {
        if (string.IsNullOrEmpty(stamp))
        {
            return null;
        }
        var dot = stamp.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            var end = dot + 1;
            while (end < stamp.Length && char.IsAsciiDigit(stamp[end]))
            {
                end++;
            }
            if (end - dot - 1 > 7)
            {
                stamp = string.Concat(stamp.AsSpan(0, dot + 8), stamp.AsSpan(end));
            }
        }
        return DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date)
            ? date
            : null;
    }

    private static long Count(JsonElement usage, string key) =>
        usage.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var count)
            ? count
            : 0;

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
