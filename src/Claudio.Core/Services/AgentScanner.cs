using System.Globalization;
using System.Text.Json;
using Claudio.Core.Models;

namespace Claudio.Core.Services;

/// <summary>One pass over Claude Code's files, as Claudy's <c>AgentScanning</c>: the view model's only source.</summary>
public interface IAgentScanning
{
    Task<ClaudeActivity> ScanActivityAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Finds the Claude Code sessions open on this PC and the subagents they run, from the files
/// Claude Code keeps anyway, as Claudy's <c>AgentScanner</c>:
/// <list type="bullet">
/// <item><c>&lt;config&gt;/sessions/&lt;pid&gt;.json</c>, one per open Claude Code process, with the session's
/// id, name and status: at work, at its prompt, or waiting on the user. A file whose process is
/// gone is a session that closed without cleaning up.</item>
/// <item><c>&lt;projects&gt;/&lt;folder&gt;/&lt;session&gt;.jsonl</c>, the session's transcript, for the last thing
/// the user asked it.</item>
/// <item><c>&lt;projects&gt;/&lt;folder&gt;/&lt;session&gt;/subagents/agent-&lt;id&gt;.meta.json</c>, written when an
/// agent is launched (its description) and again when it finishes (<c>finished</c>).</item>
/// <item><c>agent-&lt;id&gt;.jsonl</c> beside it, the agent's transcript: one line per message, its tool
/// calls among them.</item>
/// </list>
/// Nothing is installed in Claude Code for this. As with <see cref="TranscriptScanner"/>, a cursor
/// per transcript means each pass reads only what was appended since the previous one: an agent's
/// transcript passes ten megabytes on a long task.
///
/// Claudy's is an actor; here one lock serialises the passes, and <see cref="ScanActivityAsync"/>
/// runs a pass on the thread pool, so no caller's thread waits on the disk.
/// </summary>
public sealed class AgentScanner : IAgentScanning
{
    /// <summary>
    /// A finished agent keeps its row this long, marked done, so its end and what it wrote are
    /// seen even by someone who looks at the card now and then.
    /// </summary>
    public static readonly TimeSpan DoneLinger = TimeSpan.FromMinutes(10);

    /// <summary>An unfinished agent silent this long was interrupted: its session lives on, it does not.</summary>
    public static readonly TimeSpan SilenceLimit = TimeSpan.FromMinutes(30);

    /// <summary>
    /// How far back from the end of a session's transcript its last prompt is looked for, the
    /// first time: a turn's tool results run long, but rarely past this.
    /// </summary>
    public const long PromptLookback = 512 * 1024;

    /// <summary>A prompt is cut past this many characters: it only has to say which conversation it is.</summary>
    public const int PromptLength = 160;

    private static readonly byte[] ToolUseMarker = "\"tool_use\""u8.ToArray();
    private static readonly byte[] UserMarker = "\"type\":\"user\""u8.ToArray();
    private static readonly byte[] LastPromptMarker = "\"type\":\"last-prompt\""u8.ToArray();

    /// <summary>A session file, read as far as the agents need it.</summary>
    private sealed record SessionFile(
        int Pid,
        string SessionId,
        string? Cwd,
        string? Name,
        // "134392374497722792" on Windows, "Mon Nov  9 22:26:03 2026" on a Mac: when the process
        // started, which tells a reused pid apart.
        string? ProcStart,
        // Milliseconds since 1970, like StatusUpdatedAt.
        double? StartedAt,
        string? Entrypoint,
        string? Status,
        string? WaitingFor,
        double? StatusUpdatedAt);

    /// <summary>Read state for the last prompt of one session transcript.</summary>
    private sealed class PromptCursor
    {
        public long Offset;

        /// <summary>The file's creation time: detects a replacement of equal or greater size, as <see cref="TranscriptScanner"/> does.</summary>
        public DateTime FileId;

        public string? Prompt;

        /// <summary>The session's own last tool call.</summary>
        public AgentAction? Action;
    }

    private sealed record Meta(string? AgentType, string? Description, bool? Finished);

    /// <summary>Read state for one transcript.</summary>
    private sealed class Transcript
    {
        public long Offset;
        public DateTime FileId;
        public DateTimeOffset? StartedAt;

        /// <summary>Tool call ids already counted: one call can be written on more than one line.</summary>
        public HashSet<string> Seen = new(StringComparer.Ordinal);

        public int Count;
        public List<AgentAction> Recent = [];
        public AgentAction? Last;
        public List<string> Edited = [];
    }

    private readonly Lock _gate = new();
    private Dictionary<string, Transcript> _transcripts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The project folder of each session, once found: looking means listing every project.</summary>
    private Dictionary<string, string> _folders = new(StringComparer.Ordinal);

    private Dictionary<string, PromptCursor> _prompts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The app each session process runs in. It does not change while the process lives.</summary>
    private Dictionary<int, HostApp?> _hosts = [];

    private readonly Func<string> _sessionsDirectory;
    private readonly Func<IReadOnlyList<string>> _projectsDirectories;

    /// <summary>
    /// Whether the process a session file names is still that session: its pid, and the start
    /// time the file records when it does.
    /// </summary>
    private readonly Func<int, DateTimeOffset?, bool> _isLive;

    private readonly Func<int, HostApp?> _hostApp;
    private readonly Func<DateTimeOffset> _now;

    public AgentScanner(Func<string> sessionsDirectory, Func<IReadOnlyList<string>> projectsDirectories,
                        Func<int, DateTimeOffset?, bool> isLive, Func<int, HostApp?>? hostApp = null,
                        Func<DateTimeOffset>? now = null)
    {
        _sessionsDirectory = sessionsDirectory;
        _projectsDirectories = projectsDirectories;
        _isLive = isLive;
        _hostApp = hostApp ?? (_ => null);
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>The scanner the App runs: Claude Code's folders for this user, Windows' word on each pid.</summary>
    public static AgentScanner Create(ClaudeHome home, ProcessIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(home);
        ArgumentNullException.ThrowIfNull(identity);
        return new AgentScanner(() => home.SessionsDirectory, () => home.ProjectsDirectories, identity.IsSession, identity.HostApp);
    }

    public Task<ClaudeActivity> ScanActivityAsync(CancellationToken cancellationToken = default) =>
        Task.Run(ScanActivity, cancellationToken);

    /// <summary>The agents of every open session, oldest first.</summary>
    public IReadOnlyList<RunningAgent> Scan() => ScanActivity().Agents;

    /// <summary>Every open session, and the agents they run, oldest first.</summary>
    public ClaudeActivity ScanActivity()
    {
        lock (_gate)
        {
            var moment = _now();
            var agents = new List<RunningAgent>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var readPrompts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // A session open in two processes is listed once, and its agents once.
            var sessions = Unique(LiveSessions());
            var listed = new List<ClaudeSession>();
            foreach (var (session, file) in sessions)
            {
                string? prompt = null;
                AgentAction? action = null;
                if (ProjectFolder(session.Id) is { } project)
                {
                    var transcript = Path.Combine(project, $"{session.Id}.jsonl");
                    readPrompts.Add(transcript);
                    var read = LastPrompt(transcript);
                    prompt = read.Prompt;
                    action = read.Action;
                    var subagents = Path.Combine(project, session.Id, "subagents");
                    foreach (var path in TranscriptPaths(subagents))
                    {
                        if (visited.Add(path) && Agent(path, session, moment) is { } agent)
                        {
                            agents.Add(agent);
                        }
                    }
                }
                listed.Add(Session(file, session.ProcessStartedAt, session.Name, Host(session.Pid), prompt, action));
            }
            _transcripts = _transcripts.Where(pair => visited.Contains(pair.Key))
                                       .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            _prompts = _prompts.Where(pair => readPrompts.Contains(pair.Key))
                               .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var open = listed.Select(session => session.Id).ToHashSet(StringComparer.Ordinal);
            _folders = _folders.Where(pair => open.Contains(pair.Key))
                               .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var pids = listed.Select(session => session.Pid).ToHashSet();
            _hosts = _hosts.Where(pair => pids.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
            return new ClaudeActivity
            {
                Sessions = listed,
                Agents = agents.OrderBy(agent => agent.StartedAt).ThenBy(agent => agent.Id, StringComparer.Ordinal).ToList(),
            };
        }
    }

    // MARK: - Sessions

    /// <summary>The session files whose process still runs, read as far as the agents need them.</summary>
    private List<(ClaudeSession Session, SessionFile File)> LiveSessions()
    {
        var found = new List<(ClaudeSession, SessionFile)>();
        string[] paths;
        try
        {
            paths = Directory.GetFiles(_sessionsDirectory(), "*.json");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return found;
        }
        foreach (var path in paths)
        {
            // "*.json" also matches longer extensions on Windows' short names: checked again.
            if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || ReadSessionFile(path) is not { } file)
            {
                continue;
            }
            var startedAt = ProcessIdentity.DateFromProcStart(file.ProcStart);
            if (!_isLive(file.Pid, startedAt))
            {
                continue;
            }
            var name = new[] { file.Name, FolderName(file.Cwd) }.FirstOrDefault(name => !string.IsNullOrEmpty(name)) ?? "session";
            found.Add((Session(file, startedAt, name, host: null, prompt: null), file));
        }
        return found;
    }

    private static SessionFile? ReadSessionFile(string path)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("pid", out var pid) || pid.ValueKind != JsonValueKind.Number || !pid.TryGetInt32(out var number)
                || Text(root, "sessionId") is not { } sessionId)
            {
                return null;
            }
            var procStart = root.TryGetProperty("procStart", out var start) && start.ValueKind == JsonValueKind.Number
                ? start.GetRawText()
                : Text(root, "procStart");
            return new SessionFile(number, sessionId, Text(root, "cwd"), Text(root, "name"), procStart, Number(root, "startedAt"),
                                   Text(root, "entrypoint"), Text(root, "status"), Text(root, "waitingFor"), Number(root, "statusUpdatedAt"));
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    /// <summary>One entry per session id: the process whose status changed last speaks for it.</summary>
    private static List<(ClaudeSession Session, SessionFile File)> Unique(List<(ClaudeSession Session, SessionFile File)> sessions)
    {
        var kept = new Dictionary<string, (ClaudeSession Session, SessionFile File)>(StringComparer.Ordinal);
        foreach (var entry in sessions)
        {
            var since = entry.File.StatusUpdatedAt ?? 0;
            if (kept.TryGetValue(entry.Session.Id, out var held) && (held.File.StatusUpdatedAt ?? 0) >= since)
            {
                continue;
            }
            kept[entry.Session.Id] = entry;
        }
        return kept.Values.OrderBy(entry => entry.Session.OpenedAt ?? DateTimeOffset.MinValue)
                          .ThenBy(entry => entry.Session.Id, StringComparer.Ordinal)
                          .ToList();
    }

    private static ClaudeSession Session(SessionFile file, DateTimeOffset? startedAt, string name, HostApp? host,
                                         string? prompt, AgentAction? action = null) => new()
                                         {
                                             Id = file.SessionId,
                                             Pid = file.Pid,
                                             ProcessStartedAt = startedAt,
                                             Name = name,
                                             Cwd = file.Cwd,
                                             OpenedAt = Milliseconds(file.StartedAt),
                                             Status = ClaudeSession.StateOf(file.Status),
                                             WaitingFor = file.WaitingFor,
                                             StatusSince = Milliseconds(file.StatusUpdatedAt),
                                             IsBackground = ClaudeSession.IsBackgroundEntrypoint(file.Entrypoint),
                                             Host = host,
                                             LastPrompt = prompt,
                                             LastAction = action,
                                         };

    private HostApp? Host(int pid)
    {
        if (_hosts.TryGetValue(pid, out var known))
        {
            return known;
        }
        var found = _hostApp(pid);
        _hosts[pid] = found;
        return found;
    }

    /// <summary>
    /// The project folder is named after the session's working directory, with every character
    /// but letters and digits replaced: rebuilding that name would break on the first change of
    /// rule, so the folder is looked for instead. It holds the session's transcript and, once it
    /// launches one, a folder for its agents.
    /// </summary>
    private string? ProjectFolder(string session)
    {
        if (_folders.TryGetValue(session, out var known) && Directory.Exists(known))
        {
            return known;
        }
        foreach (var projects in _projectsDirectories())
        {
            string[] folders;
            try
            {
                folders = Directory.Exists(projects) ? Directory.GetDirectories(projects) : [];
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (var project in folders)
            {
                if (File.Exists(Path.Combine(project, $"{session}.jsonl")) || Directory.Exists(Path.Combine(project, session)))
                {
                    _folders[session] = project;
                    return project;
                }
            }
        }
        return null;
    }

    /// <summary>Workflows nest their agents one folder deeper, hence the walk.</summary>
    private static List<string> TranscriptPaths(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return [];
        }
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        };
        try
        {
            return Directory.EnumerateFiles(folder, "agent-*.jsonl", options)
                            .Where(path => path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
                            .ToList();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // MARK: - Last prompt

    /// <summary>
    /// The last thing the user typed in a session and the session's own last tool call, read from
    /// its transcript: the first time from <see cref="PromptLookback"/> before its end, then only
    /// what was appended.
    /// </summary>
    private PromptCursor LastPrompt(string path)
    {
        if (!_prompts.TryGetValue(path, out var state))
        {
            state = new PromptCursor();
            _prompts[path] = state;
        }
        byte[] data;
        long start;
        try
        {
            var created = File.GetCreationTimeUtc(path);
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var size = stream.Length;
            if ((state.FileId != default && state.FileId != created) || size < state.Offset)
            {
                state = new PromptCursor();
                _prompts[path] = state;
            }
            state.FileId = created;

            var floor = size > PromptLookback ? size - PromptLookback : 0;
            start = Math.Max(state.Offset, floor);
            if (size <= start)
            {
                return state;
            }
            stream.Seek(start, SeekOrigin.Begin);
            data = new byte[size - start];
            stream.ReadExactly(data);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return state;
        }

        var (lines, consumed) = Lines(data);
        // Jumping ahead lands anywhere in a line: what comes before the first newline is not one.
        if (start > state.Offset && start > 0)
        {
            var firstNewline = Array.IndexOf(data, (byte)'\n');
            firstNewline = firstNewline < 0 ? data.Length : firstNewline;
            lines.RemoveAll(line => line.Start <= firstNewline);
        }
        state.Offset = start + consumed;
        foreach (var (lineStart, length) in lines)
        {
            var line = data.AsSpan(lineStart, length);
            if (line.IndexOf(ToolUseMarker) >= 0)
            {
                if (LastCall(line) is { } action)
                {
                    state.Action = action;
                }
            }
            else if (line.IndexOf(LastPromptMarker) >= 0 || line.IndexOf(UserMarker) >= 0)
            {
                if (Prompt(line) is { } prompt)
                {
                    state.Prompt = prompt;
                }
            }
        }
        return state;
    }

    /// <summary>
    /// The last tool call of one line of a session's transcript, when the session made it itself:
    /// its agents' calls are in their own transcripts.
    /// </summary>
    private static AgentAction? LastCall(ReadOnlySpan<byte> line)
    {
        using var document = Parse(line);
        if (document is null)
        {
            return null;
        }
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Text(root, "type") != "assistant" || IsTrue(root, "isSidechain")
            || TranscriptScanner.Timestamp(Text(root, "timestamp")) is not { } date
            || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return null;
        }
        JsonElement? last = null;
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object && Text(block, "type") == "tool_use")
            {
                last = block;
            }
        }
        return last is { } call && Text(call, "name") is { } tool ? Action(call, tool, date) : null;
    }

    /// <summary>
    /// What the user typed, from one transcript line. Claude Code writes it down itself, often, in
    /// a <c>last-prompt</c> line; failing that, a user message that is neither a tool result, nor
    /// what Claude Code adds on its own (reminders, the IDE's context, slash command echoes, all in
    /// tags), nor an interruption notice, nor a summary of a compacted conversation.
    /// </summary>
    public static string? Prompt(ReadOnlySpan<byte> line)
    {
        using var document = Parse(line);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }
        var root = document.RootElement;
        if (Text(root, "type") == "last-prompt")
        {
            return Text(root, "lastPrompt") is { } typed ? Typed([typed]) : null;
        }
        if (Text(root, "type") != "user" || IsTrue(root, "isMeta") || IsTrue(root, "isSidechain") || IsTrue(root, "isCompactSummary")
            || !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content))
        {
            return null;
        }
        var texts = new List<string>();
        if (content.ValueKind == JsonValueKind.String)
        {
            texts.Add(content.GetString()!);
        }
        else if (content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                var type = Text(block, "type");
                if (type == "tool_result")
                {
                    return null;
                }
                if (type == "text" && Text(block, "text") is { } text)
                {
                    texts.Add(text);
                }
            }
        }
        else
        {
            return null;
        }
        return Typed(texts);
    }

    /// <summary>The last of these texts a person typed, on one line, cut past <see cref="PromptLength"/>.</summary>
    private static string? Typed(List<string> texts)
    {
        var typed = texts.Select(text => text.Trim())
                         .LastOrDefault(text => text.Length > 0 && !text.StartsWith('<')
                                                && !text.StartsWith("[Request interrupted", StringComparison.Ordinal));
        if (typed is null)
        {
            return null;
        }
        var oneLine = string.Join(' ', typed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        var length = new StringInfo(oneLine).LengthInTextElements;
        return length > PromptLength ? new StringInfo(oneLine).SubstringByTextElements(0, PromptLength) + "…" : oneLine;
    }

    // MARK: - One agent

    private RunningAgent? Agent(string path, ClaudeSession session, DateTimeOffset now)
    {
        DateTimeOffset modified;
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }
            modified = new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        var meta = MetaBeside(path);
        var isFinished = meta?.Finished == true;
        if (now - modified >= (isFinished ? DoneLinger : SilenceLimit))
        {
            _transcripts.Remove(path);
            return null;
        }

        var state = Read(path, now);
        var id = Path.GetFileNameWithoutExtension(path)["agent-".Length..];
        var title = new[] { meta?.Description, meta?.AgentType }.FirstOrDefault(text => !string.IsNullOrEmpty(text)) ?? "Agent";
        return new RunningAgent
        {
            Id = id,
            SessionId = session.Id,
            SessionPid = session.Pid,
            SessionStartedAt = session.ProcessStartedAt,
            SessionName = session.Name,
            Title = title,
            StartedAt = state.StartedAt ?? modified,
            LastActivity = modified,
            IsFinished = isFinished,
            ActionCount = state.Count,
            Recent = state.Recent,
            LastAction = state.Last,
            EditedFiles = state.Edited,
        };
    }

    private static Meta? MetaBeside(string transcript)
    {
        var path = Path.ChangeExtension(transcript, ".meta.json");
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            bool? finished = root.TryGetProperty("finished", out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? value.GetBoolean()
                : null;
            return new Meta(Text(root, "agentType"), Text(root, "description"), finished);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // MARK: - Reading

    private Transcript Read(string path, DateTimeOffset now)
    {
        if (!_transcripts.TryGetValue(path, out var state))
        {
            state = new Transcript();
        }
        try
        {
            var created = File.GetCreationTimeUtc(path);
            byte[] data;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var size = stream.Length;
                if ((state.FileId != default && state.FileId != created) || size < state.Offset)
                {
                    state = new Transcript();
                }
                state.FileId = created;
                if (size <= state.Offset)
                {
                    return state;
                }
                stream.Seek(state.Offset, SeekOrigin.Begin);
                data = new byte[size - state.Offset];
                stream.ReadExactly(data);
            }

            var (lines, consumed) = Lines(data);
            state.Offset += consumed;
            foreach (var (start, length) in lines)
            {
                var line = data.AsSpan(start, length);
                if (state.StartedAt is null || line.IndexOf(ToolUseMarker) >= 0)
                {
                    Absorb(line, state);
                }
            }
            return state;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            return state;
        }
        finally
        {
            var cutoff = now - RunningAgent.TrackSpan;
            state.Recent.RemoveAll(action => action.Date < cutoff);
            _transcripts[path] = state;
        }
    }

    /// <summary>Picks the start date off the first dated line, and every tool call off the others.</summary>
    private static void Absorb(ReadOnlySpan<byte> line, Transcript state)
    {
        using var document = Parse(line);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Object)
        {
            return;
        }
        var root = document.RootElement;
        if (TranscriptScanner.Timestamp(Text(root, "timestamp")) is not { } date)
        {
            return;
        }
        state.StartedAt ??= date;

        if (!root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object
            || !message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return;
        }
        foreach (var block in content.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object || Text(block, "type") != "tool_use"
                || Text(block, "id") is not { } id || Text(block, "name") is not { } tool || !state.Seen.Add(id))
            {
                continue;
            }
            var action = Action(block, tool, date);
            state.Count++;
            state.Last = action;
            state.Recent.Add(action);
            if (action.Kind == AgentActionKind.Edit && action.File is { } file && !state.Edited.Contains(file))
            {
                state.Edited.Add(file);
            }
        }
    }

    /// <summary>One <c>tool_use</c> block as the agents list reads it: its kind, its file, what it is for.</summary>
    private static AgentAction Action(JsonElement block, string tool, DateTimeOffset date)
    {
        var input = block.TryGetProperty("input", out var value) && value.ValueKind == JsonValueKind.Object ? value : (JsonElement?)null;
        var path = input is { } fields ? Text(fields, "file_path") ?? Text(fields, "notebook_path") : null;
        var reading = AgentWorks.Read(tool, input is { } a ? Text(a, "command") : null, path, input is { } b ? Text(b, "skill") : null);
        return new AgentAction(date, AgentActionKinds.Of(tool), tool, LastPathComponent(path), reading.Work, reading.Detail);
    }

    /// <summary>
    /// The complete lines of <paramref name="data"/>, as offsets and lengths, and how many bytes
    /// they span. The tail after the last newline is a write in progress, left for the next pass,
    /// unless its JSON already parses: some transcripts end without a newline.
    /// </summary>
    private static (List<(int Start, int Length)> Lines, long Consumed) Lines(byte[] data)
    {
        var lines = new List<(int, int)>();
        var span = data.AsSpan();
        var start = 0;
        while (start < span.Length)
        {
            var newline = span[start..].IndexOf((byte)'\n');
            if (newline < 0)
            {
                break;
            }
            if (newline > 0)
            {
                lines.Add((start, newline));
            }
            start += newline + 1;
        }
        if (start >= span.Length)
        {
            return (lines, span.Length);
        }
        using var tail = Parse(span[start..]);
        if (tail is null)
        {
            return (lines, start);
        }
        lines.Add((start, span.Length - start));
        return (lines, span.Length);
    }

    private static JsonDocument? Parse(ReadOnlySpan<byte> line)
    {
        try
        {
            return JsonDocument.Parse(line.ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>A file's name from its path, whichever separator the path uses.</summary>
    private static string? LastPathComponent(string? path)
    {
        if (path is null)
        {
            return null;
        }
        var trimmed = path.TrimEnd('/', '\\');
        return trimmed.Length == 0 ? path : trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
    }

    private static string? FolderName(string? cwd)
    {
        if (cwd is null)
        {
            return null;
        }
        var trimmed = cwd.TrimEnd('/', '\\');
        var name = trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
        return name.Length == 0 ? null : name;
    }

    /// <summary>The earliest and latest instants a date holds, in milliseconds since 1970.</summary>
    private static readonly double EarliestMilliseconds = (DateTimeOffset.MinValue - DateTimeOffset.UnixEpoch).TotalMilliseconds;
    private static readonly double LatestMilliseconds = (DateTimeOffset.MaxValue - DateTimeOffset.UnixEpoch).TotalMilliseconds;

    /// <summary>
    /// A date written in milliseconds. A number no date can hold, in a damaged file, reads as no
    /// date: Swift's <c>Date(timeIntervalSince1970:)</c> never fails, and one bad field must not
    /// stop the whole pass.
    /// </summary>
    private static DateTimeOffset? Milliseconds(double? value) =>
        value is { } milliseconds && double.IsFinite(milliseconds) && milliseconds > EarliestMilliseconds && milliseconds < LatestMilliseconds
            ? DateTimeOffset.UnixEpoch.AddTicks((long)(milliseconds * TimeSpan.TicksPerMillisecond))
            : null;

    /// <summary>
    /// A string field. A lone half of a surrogate pair, which JavaScript writes when a string was
    /// cut through an emoji, reads as no text rather than throwing: Swift's reading skips that
    /// value, and the lines after it must still be read.
    /// </summary>
    private static string? Text(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        try
        {
            return value.GetString();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static double? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)
            ? number
            : null;

    private static bool IsTrue(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
