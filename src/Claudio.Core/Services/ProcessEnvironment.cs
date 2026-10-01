namespace Claudio.Core.Services;

/// <summary>
/// Reads the Claude Code markers out of a process's environment, as Claudy's
/// <c>ProcessEnvironment</c>.
///
/// The environment is inherited by every descendant and outlives the session that created it,
/// so it is the only attribution signal that still holds once the session is gone. A process
/// environment routinely carries tokens: nothing but the presence flag and the project directory
/// ever leaves this type, and the raw block is walked in place rather than cut into strings.
/// </summary>
public static class ProcessEnvironment
{
    public sealed record Markers(bool IsClaude, string? ProjectDirectory);

    private const string ClaudeCode = "CLAUDECODE";
    private const string Entrypoint = "CLAUDE_CODE_ENTRYPOINT";
    private const string ProjectDirectory = "CLAUDE_PROJECT_DIR";

    /// <summary>Markers from <c>NAME=value</c> entries.</summary>
    public static Markers Parse(IEnumerable<string> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var reading = new Reading();
        foreach (var entry in entries)
        {
            reading.Read(entry);
        }
        return reading.Result;
    }

    /// <summary>
    /// Markers from a Windows environment block: <c>NAME=value</c> entries, each ended by a NUL,
    /// the block by an empty one. Only the PEB says where a block ends, and a read can come back
    /// short: an entry left without its NUL is cut, so it is dropped rather than half read.
    /// </summary>
    public static Markers ParseBlock(ReadOnlySpan<char> block)
    {
        var reading = new Reading();
        while (block.IndexOf('\0') is var end and > 0)
        {
            reading.Read(block[..end]);
            block = block[(end + 1)..];
        }
        return reading.Result;
    }

    /// <summary>
    /// Windows names are case-insensitive. Entries that open with <c>=</c> are the shell's
    /// per-drive directories (<c>=C:=C:\Dev</c>), not variables.
    /// </summary>
    private ref struct Reading
    {
        private bool _isClaude;
        private string? _projectDirectory;

        public readonly Markers Result => new(_isClaude, _projectDirectory);

        public void Read(ReadOnlySpan<char> entry)
        {
            var equals = entry.IndexOf('=');
            if (equals <= 0)
            {
                return;
            }
            var name = entry[..equals];
            if (name.Equals(ClaudeCode, StringComparison.OrdinalIgnoreCase)
                || name.Equals(Entrypoint, StringComparison.OrdinalIgnoreCase))
            {
                _isClaude = true;
            }
            else if (name.Equals(ProjectDirectory, StringComparison.OrdinalIgnoreCase))
            {
                _isClaude = true;
                _projectDirectory = entry[(equals + 1)..].ToString();
            }
        }
    }
}
