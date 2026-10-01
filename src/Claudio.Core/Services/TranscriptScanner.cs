using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Claudio.Core.Services;

/// <summary>One model response recorded in a transcript.</summary>
/// <param name="Weight">
/// What the tokens weigh against the quota, in dollars at list price. Cache reads are most of the
/// volume but cost a tenth of an input token, and an Opus token five Haiku ones: ranking by raw
/// tokens put a background Haiku job above real work.
/// </param>
/// <param name="Cwd">Directory the model was working in.</param>
/// <param name="IsSidechain">A subagent response. Its tokens count, but it is not a user session.</param>
/// <param name="DedupKey"><c>message.id:requestId</c>; null when the line has no identifiers and counts on its own.</param>
public sealed record TranscriptEntry(
    DateTimeOffset Date,
    string Model,
    long Tokens,
    double Weight,
    string Cwd,
    string SessionId,
    bool IsSidechain,
    string? DedupKey)
{
    /// <summary>
    /// Project the response belongs to: a repository root, or the directory itself when no
    /// repository holds it. Resolved by <see cref="TranscriptScanner.Scan"/> once the whole session is known.
    /// </summary>
    public string Project { get; init; } = Cwd;
}

/// <summary>The transcripts folder exists but will not be read (permissions, disk).</summary>
public sealed class UsageDataException(string message) : IOException(message);

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
    private readonly ProjectResolver _resolver;
    private readonly Dictionary<string, FileState> _files = new(StringComparer.OrdinalIgnoreCase);

    public TranscriptScanner(Func<IReadOnlyList<string>> projectsDirectories, Func<DateTimeOffset>? now = null,
                             ProjectResolver? resolver = null)
    {
        _projectsDirectories = projectsDirectories;
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _resolver = resolver ?? new ProjectResolver(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
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
        return AssignProjects(unique.Values).OrderBy(entry => entry.Date).ToList();
    }

    /// <summary>
    /// Attributes each response to a repository. A directory outside any repository (the
    /// scratchpad a session <c>cd</c>s into, a deleted temporary folder) takes the repository the
    /// rest of its session worked in, and only failing that stands as a project of its own.
    /// </summary>
    private List<TranscriptEntry> AssignProjects(IEnumerable<TranscriptEntry> responses)
    {
        var entries = responses.ToList();
        var roots = entries.Select(entry => entry.Cwd).Distinct(StringComparer.Ordinal)
                           .ToDictionary(cwd => cwd, _resolver.RepositoryRoot, StringComparer.Ordinal);

        var sessionWeights = new Dictionary<string, Dictionary<string, double>>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (roots[entry.Cwd] is not { } root)
            {
                continue;
            }
            if (!sessionWeights.TryGetValue(entry.SessionId, out var weights))
            {
                weights = new Dictionary<string, double>(StringComparer.Ordinal);
                sessionWeights[entry.SessionId] = weights;
            }
            weights[root] = weights.GetValueOrDefault(root) + entry.Weight;
        }
        var sessionRoot = sessionWeights.ToDictionary(pair => pair.Key, pair => pair.Value.MaxBy(weight => weight.Value).Key,
                                                      StringComparer.Ordinal);

        return entries.Select(entry => entry with
        {
            Project = roots[entry.Cwd] ?? sessionRoot.GetValueOrDefault(entry.SessionId) ?? Spelling(entry.Cwd),
        }).ToList();
    }

    /// <summary>A folder outside any repository, spelt once: <c>g:	mp</c> and <c>G:	mp</c> are one project.</summary>
    private static string Spelling(string cwd)
    {
        try
        {
            return Path.IsPathFullyQualified(cwd) ? ProjectResolver.Normalize(cwd) : cwd;
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return cwd;
        }
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

    /// <summary>
    /// Transcripts under every projects folder, the subagent ones below each session included.
    /// The first folder that exists must be readable, or the error surfaces; any later one is a
    /// fallback, and a permission quirk there must not blank a folder that reads fine.
    /// </summary>
    private List<string> TranscriptFiles(DateTimeOffset cutoff)
    {
        var found = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var hasReadAFolder = false;
        foreach (var folder in _projectsDirectories())
        {
            if (!Directory.Exists(folder) || !visited.Add(Path.GetFullPath(folder)))
            {
                continue;
            }
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.Hidden | FileAttributes.System };
            try
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.jsonl", options))
                {
                    if (File.GetLastWriteTimeUtc(file) >= cutoff.UtcDateTime)
                    {
                        found.Add(file);
                    }
                }
                hasReadAFolder = true;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException && !hasReadAFolder)
            {
                throw new UsageDataException("Could not read the transcripts folder (permissions?).");
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
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
                || Text(message, "model") is not { } model || !ModelName.IsReal(model)
                || Timestamp(Text(root, "timestamp")) is not { } date)
            {
                return null;
            }

            long input = Count(usage, "input_tokens"), output = Count(usage, "output_tokens");
            long cacheWrite = Count(usage, "cache_creation_input_tokens"), cacheRead = Count(usage, "cache_read_input_tokens");
            var tokens = input + output + cacheWrite + cacheRead;
            if (tokens <= 0)
            {
                return null;
            }

            // A one-hour cache write costs twice the input price, a five-minute one 1.25 times. The
            // split is absent from older lines, which are priced as five-minute writes.
            var longWrites = usage.TryGetProperty("cache_creation", out var creation) && creation.ValueKind == JsonValueKind.Object
                ? Count(creation, "ephemeral_1h_input_tokens")
                : 0;
            var inputUnits = input + (5.0 * output) + (1.25 * Math.Max(cacheWrite - longWrites, 0)) + (2.0 * longWrites) + (0.1 * cacheRead);
            var weight = inputUnits * ModelName.InputPrice(model) / 1_000_000;

            var messageId = Text(message, "id");
            var requestId = Text(root, "requestId");
            var key = !string.IsNullOrEmpty(messageId) && !string.IsNullOrEmpty(requestId) ? $"{messageId}:{requestId}" : null;
            var sidechain = root.TryGetProperty("isSidechain", out var side) && side.ValueKind == JsonValueKind.True;
            return new TranscriptEntry(date, model, tokens, weight, Text(root, "cwd") ?? string.Empty,
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
