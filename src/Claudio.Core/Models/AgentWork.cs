using System.Text;

namespace Claudio.Core.Models;

/// <summary>The area of the agent room a kind of work sits in.</summary>
public enum WorkFamily
{
    Writing,
    Commands,
    Research,
}

/// <summary>
/// What one tool call is for, as the agent room shows it on a desk's screen, as Claudy's
/// <c>AgentWork</c>. Read from what the call says about itself (the tool, the command it ran, the
/// file it touched), never from a list of known skills: a skill nobody has heard of is told apart
/// by the commands it runs.
/// </summary>
public enum AgentWork
{
    Code,
    Docs,
    Plan,
    Design,
    Search,
    Review,
    Web,
    Terminal,
    Tests,
    Build,
    Benchmark,
    Install,
    Git,
    Data,
    Infra,
    Deploy,
    Security,
    Delegating,
    Skill,
    Mcp,
    Other,
}

/// <summary>A tool call read for its work, and the few words that name it.</summary>
public readonly record struct WorkReading(AgentWork Work, string? Detail);

public static class AgentWorks
{
    public static IReadOnlyList<WorkFamily> Families { get; } = [WorkFamily.Writing, WorkFamily.Commands, WorkFamily.Research];

    /// <summary>The name on the area's sign.</summary>
    public static string Title(this WorkFamily family) => family switch
    {
        WorkFamily.Writing => "Writing",
        WorkFamily.Commands => "Commands",
        _ => "Research",
    };

    /// <summary>
    /// The area this work is done in. A call that says nothing of the work (launching an agent,
    /// loading a skill) has none: the agent stays where it sits.
    /// </summary>
    public static WorkFamily? Family(this AgentWork work) => work switch
    {
        AgentWork.Code or AgentWork.Docs or AgentWork.Plan or AgentWork.Design => WorkFamily.Writing,
        AgentWork.Search or AgentWork.Review or AgentWork.Web => WorkFamily.Research,
        AgentWork.Terminal or AgentWork.Tests or AgentWork.Build or AgentWork.Benchmark or AgentWork.Install
            or AgentWork.Git or AgentWork.Data or AgentWork.Infra or AgentWork.Deploy or AgentWork.Security => WorkFamily.Commands,
        _ => null,
    };

    /// <summary>One word for it, before the file or the command.</summary>
    public static string Label(this AgentWork work) => work switch
    {
        AgentWork.Plan => "spec",
        AgentWork.Mcp => "MCP",
        AgentWork.Other => "working",
        _ => work.ToString().ToLowerInvariant(),
    };

    /// <summary>The more telling of two kinds of command, when one line chains several.</summary>
    private static int Weight(this AgentWork work) => work switch
    {
        AgentWork.Security => 12,
        AgentWork.Benchmark => 11,
        AgentWork.Tests => 10,
        AgentWork.Build => 9,
        AgentWork.Deploy => 8,
        AgentWork.Infra => 7,
        AgentWork.Install => 6,
        AgentWork.Data => 5,
        AgentWork.Review => 4,
        AgentWork.Git => 3,
        AgentWork.Web => 2,
        AgentWork.Search => 1,
        _ => 0,
    };

    // Reading a call

    /// <summary>
    /// The work behind one tool call, and the few words that name it: the command's first words,
    /// the skill's name, the MCP server's. Claude Code on Windows runs its commands through Git
    /// Bash, or through PowerShell when that tool is on: both are read as a shell line.
    /// </summary>
    public static WorkReading Read(string tool, string? command = null, string? path = null, string? skill = null)
    {
        ArgumentNullException.ThrowIfNull(tool);
        return tool switch
        {
            "Edit" or "Write" or "MultiEdit" or "NotebookEdit" => new(Written(path), null),
            "Read" or "Grep" or "Glob" or "LS" or "NotebookRead" => new(AgentWork.Search, null),
            "WebFetch" or "WebSearch" => new(AgentWork.Web, null),
            "Agent" or "Task" => new(AgentWork.Delegating, null),
            "Skill" => new(AgentWork.Skill, string.IsNullOrEmpty(skill) ? null : skill),
            "Bash" or "PowerShell" => command is null ? new(AgentWork.Terminal, null) : Ran(command),
            "BashOutput" or "KillShell" or "KillBash" => new(AgentWork.Terminal, null),
            _ => Served(tool),
        };
    }

    private static readonly string[] Browsing = ["browser", "chrome", "devtools", "playwright"];

    /// <summary>A tool an MCP server brings, under the name its plugin chose: <c>mcp__&lt;server&gt;__&lt;tool&gt;</c>.</summary>
    private static WorkReading Served(string tool)
    {
        if (!tool.StartsWith("mcp__", StringComparison.Ordinal))
        {
            return new(AgentWork.Other, null);
        }
        var rest = tool["mcp__".Length..];
        var end = rest.IndexOf("__", StringComparison.Ordinal);
        var server = end < 0 ? rest : rest[..end];
        var name = tool.ToLowerInvariant();
        if (Browsing.Any(word => name.Contains(word, StringComparison.Ordinal)))
        {
            return new(AgentWork.Web, server);
        }
        return name.Contains("figma", StringComparison.Ordinal) ? new(AgentWork.Design, server) : new(AgentWork.Mcp, server);
    }

    private static readonly string[] PlanNames = ["plan.md", "tasks.md", "spec.md", "prd.md", "constitution.md"];
    private static readonly string[] PlanFolders = ["/specs/", "/spec/", "/plans/"];

    /// <summary>
    /// The work of writing a file, told by where the file is and what it is. A Windows path is read
    /// with forward slashes, so <c>.github\workflows\</c> is the same folder as on a Mac.
    /// </summary>
    private static AgentWork Written(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return AgentWork.Code;
        }
        var lowered = path.Replace('\\', '/').ToLowerInvariant();
        var name = lowered[(lowered.LastIndexOf('/') + 1)..];
        var dot = name.LastIndexOf('.');
        var extension = dot > 0 ? name[(dot + 1)..] : string.Empty;
        if (lowered.Contains(".github/workflows/", StringComparison.Ordinal))
        {
            return AgentWork.Deploy;
        }
        if (extension is "tf" or "tfvars" or "hcl" || name == "dockerfile" || name.StartsWith("docker-compose", StringComparison.Ordinal))
        {
            return AgentWork.Infra;
        }
        if (extension == "sql" || lowered.Contains("/migrations/", StringComparison.Ordinal))
        {
            return AgentWork.Data;
        }
        if (extension is "css" or "scss" or "sass" or "less" or "svg")
        {
            return AgentWork.Design;
        }
        if (extension is "md" or "mdx" or "rst" or "txt" or "adoc")
        {
            var planned = PlanNames.Contains(name) || PlanFolders.Any(folder => lowered.Contains(folder, StringComparison.Ordinal));
            return planned ? AgentWork.Plan : AgentWork.Docs;
        }
        return AgentWork.Code;
    }

    // Reading a command

    /// <summary>
    /// Programs that say what they do by their name alone. Claudy's table, and the few a Windows PC
    /// adds: msbuild builds; winget, choco and scoop install.
    /// </summary>
    private static readonly Dictionary<string, AgentWork> Programs = new(StringComparer.Ordinal)
    {
        ["trivy"] = AgentWork.Security, ["semgrep"] = AgentWork.Security, ["gitleaks"] = AgentWork.Security,
        ["snyk"] = AgentWork.Security, ["bandit"] = AgentWork.Security, ["pip-audit"] = AgentWork.Security,
        ["osv-scanner"] = AgentWork.Security,
        ["hyperfine"] = AgentWork.Benchmark, ["k6"] = AgentWork.Benchmark, ["wrk"] = AgentWork.Benchmark,
        ["lighthouse"] = AgentWork.Benchmark,
        ["pytest"] = AgentWork.Tests, ["jest"] = AgentWork.Tests, ["vitest"] = AgentWork.Tests, ["rspec"] = AgentWork.Tests,
        ["phpunit"] = AgentWork.Tests, ["playwright"] = AgentWork.Tests,
        ["xcodebuild"] = AgentWork.Build, ["make"] = AgentWork.Build, ["cmake"] = AgentWork.Build, ["tsc"] = AgentWork.Build,
        ["gradle"] = AgentWork.Build, ["gradlew"] = AgentWork.Build, ["mvn"] = AgentWork.Build, ["webpack"] = AgentWork.Build,
        ["ninja"] = AgentWork.Build, ["msbuild"] = AgentWork.Build,
        ["vercel"] = AgentWork.Deploy, ["netlify"] = AgentWork.Deploy, ["flyctl"] = AgentWork.Deploy,
        ["terraform"] = AgentWork.Infra, ["tofu"] = AgentWork.Infra, ["terragrunt"] = AgentWork.Infra, ["kubectl"] = AgentWork.Infra,
        ["helm"] = AgentWork.Infra, ["docker"] = AgentWork.Infra, ["ansible"] = AgentWork.Infra,
        ["ansible-playbook"] = AgentWork.Infra, ["pulumi"] = AgentWork.Infra, ["aws"] = AgentWork.Infra,
        ["gcloud"] = AgentWork.Infra, ["az"] = AgentWork.Infra,
        ["brew"] = AgentWork.Install, ["pip"] = AgentWork.Install, ["pip3"] = AgentWork.Install,
        ["winget"] = AgentWork.Install, ["choco"] = AgentWork.Install, ["scoop"] = AgentWork.Install,
        ["git"] = AgentWork.Git, ["gh"] = AgentWork.Git,
        ["psql"] = AgentWork.Data, ["mysql"] = AgentWork.Data, ["sqlite3"] = AgentWork.Data, ["dbt"] = AgentWork.Data,
        ["mongosh"] = AgentWork.Data, ["redis-cli"] = AgentWork.Data, ["alembic"] = AgentWork.Data,
        ["grep"] = AgentWork.Search, ["rg"] = AgentWork.Search, ["find"] = AgentWork.Search, ["ls"] = AgentWork.Search,
        ["cat"] = AgentWork.Search, ["head"] = AgentWork.Search, ["tail"] = AgentWork.Search, ["wc"] = AgentWork.Search,
        ["fd"] = AgentWork.Search, ["tree"] = AgentWork.Search,
        ["curl"] = AgentWork.Web, ["wget"] = AgentWork.Web,
    };

    /// <summary>Programs whose first argument decides: <c>npm test</c> is not <c>npm install</c>.</summary>
    private static readonly Dictionary<string, Dictionary<string, AgentWork>> Subcommands = new(StringComparer.Ordinal)
    {
        ["npm"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests, ["t"] = AgentWork.Tests, ["install"] = AgentWork.Install, ["i"] = AgentWork.Install, ["ci"] = AgentWork.Install, ["audit"] = AgentWork.Security },
        ["pnpm"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests, ["install"] = AgentWork.Install, ["add"] = AgentWork.Install, ["audit"] = AgentWork.Security, ["build"] = AgentWork.Build },
        ["yarn"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests, ["install"] = AgentWork.Install, ["add"] = AgentWork.Install, ["audit"] = AgentWork.Security, ["build"] = AgentWork.Build },
        ["bun"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests, ["install"] = AgentWork.Install, ["add"] = AgentWork.Install, ["build"] = AgentWork.Build },
        ["swift"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests, ["build"] = AgentWork.Build },
        ["cargo"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests, ["build"] = AgentWork.Build, ["bench"] = AgentWork.Benchmark, ["add"] = AgentWork.Install, ["install"] = AgentWork.Install, ["audit"] = AgentWork.Security },
        ["go"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests, ["build"] = AgentWork.Build, ["get"] = AgentWork.Install },
        ["dotnet"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests, ["build"] = AgentWork.Build },
        ["mvn"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests },
        ["gradle"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests },
        ["gradlew"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests },
        ["xcodebuild"] = new(StringComparer.Ordinal) { ["test"] = AgentWork.Tests },
        ["uv"] = new(StringComparer.Ordinal) { ["add"] = AgentWork.Install, ["sync"] = AgentWork.Install, ["pip"] = AgentWork.Install },
        ["bundle"] = new(StringComparer.Ordinal) { ["install"] = AgentWork.Install },
        ["docker"] = new(StringComparer.Ordinal) { ["push"] = AgentWork.Deploy },
        ["gh"] = new(StringComparer.Ordinal) { ["workflow"] = AgentWork.Deploy, ["run"] = AgentWork.Deploy, ["release"] = AgentWork.Deploy },
        ["git"] = new(StringComparer.Ordinal) { ["diff"] = AgentWork.Review, ["log"] = AgentWork.Review, ["show"] = AgentWork.Review, ["blame"] = AgentWork.Review },
        ["fly"] = new(StringComparer.Ordinal) { ["deploy"] = AgentWork.Deploy },
        ["prisma"] = new(StringComparer.Ordinal) { ["migrate"] = AgentWork.Data },
    };

    /// <summary>Words that run another program and say nothing themselves.</summary>
    private static readonly HashSet<string> Wrappers = new(StringComparer.Ordinal) { "sudo", "time", "npx", "bunx", "exec", "command", "env", "nohup" };

    /// <summary>Programs whose action comes after their options: <c>xcodebuild -scheme App test</c>.</summary>
    private static readonly HashSet<string> ActsLate = new(StringComparer.Ordinal) { "xcodebuild", "gradle", "gradlew", "mvn" };

    /// <summary>Commands that only move around.</summary>
    private static readonly HashSet<string> Moves = new(StringComparer.Ordinal) { "cd", "export", "source", "echo", "mkdir", "set", "true", "pushd", "popd" };

    private static readonly string[] PackageManagers = ["npm", "pnpm", "yarn", "bun"];

    private static readonly string[] ReviewWords = ["view", "diff", "checks", "list"];

    private static readonly string[] Separators = ["&&", "||", ";", "|", "\n"];

    private static readonly string[] WindowsSuffixes = [".exe", ".cmd", ".bat"];

    /// <summary>
    /// The work of one shell line. A line chains commands; the most telling one names it:
    /// <c>cd app &amp;&amp; npm test</c> is tests.
    /// </summary>
    public static WorkReading Ran(string command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var best = new WorkReading(AgentWork.Terminal, null);
        foreach (var segment in Segments(command))
        {
            if (One(segment) is not { } found)
            {
                continue;
            }
            if (best.Detail is null || found.Work.Weight() > best.Work.Weight())
            {
                best = found;
            }
        }
        return best;
    }

    /// <summary>
    /// The line without what its quotes hold: a commit message or a title is not a command, and
    /// must neither split the line nor name it.
    /// </summary>
    private static string Unquoted(ReadOnlySpan<char> command)
    {
        var result = new StringBuilder(command.Length);
        char? quote = null;
        foreach (var character in command)
        {
            if (quote is { } open)
            {
                if (character == open)
                {
                    quote = null;
                }
            }
            else if (character is '"' or '\'')
            {
                quote = character;
                result.Append('_');
            }
            else
            {
                result.Append(character);
            }
        }
        return result.ToString();
    }

    private static List<string[]> Segments(string command)
    {
        var separated = Unquoted(command.AsSpan(0, Math.Min(command.Length, 2_000)));
        foreach (var separator in Separators)
        {
            separated = separated.Replace(separator, "\u001F", StringComparison.Ordinal);
        }
        return separated.Split('\u001F', StringSplitOptions.RemoveEmptyEntries)
                        .Select(segment => segment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                        .ToList();
    }

    /// <summary>
    /// A program's name as the tables know it: its file name, lowered, without the <c>.exe</c>,
    /// <c>.cmd</c> or <c>.bat</c> Windows may add.
    /// </summary>
    private static string ProgramName(string word)
    {
        var name = word[(word.LastIndexOfAny(['/', '\\']) + 1)..].ToLowerInvariant();
        foreach (var suffix in WindowsSuffixes)
        {
            if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return name[..^suffix.Length];
            }
        }
        return name;
    }

    /// <summary>The work of one command of the line, null for one that only moves around (<c>cd</c>, <c>export</c>).</summary>
    private static WorkReading? One(string[] words)
    {
        var meaningful = words.SkipWhile(word => word.Contains('=', StringComparison.Ordinal) || Wrappers.Contains(word)).ToList();
        if (meaningful.Count == 0)
        {
            return null;
        }
        var program = ProgramName(meaningful[0]);
        if (Moves.Contains(program))
        {
            return null;
        }
        var arguments = meaningful.Skip(1).Select(word => word.ToLowerInvariant()).ToList();
        var named = arguments.Count > 0 && !arguments[0].StartsWith('-') ? arguments[0] : null;
        var detail = named is null ? program : $"{program} {named}";
        // `gh pr view` reads, `gh pr create` writes.
        if (program == "gh" && named == "pr" && ReviewWords.Any(arguments.Contains))
        {
            return new(AgentWork.Review, detail);
        }
        var isBench = named is "bench" or "benchmark"
            || arguments.Any(argument => argument.StartsWith("-bench", StringComparison.Ordinal) || argument.StartsWith("--bench", StringComparison.Ordinal));
        if (isBench || program.StartsWith("bench", StringComparison.Ordinal))
        {
            return new(AgentWork.Benchmark, detail);
        }
        if (PackageManagers.Contains(program) && arguments.Count > 1 && arguments[0] == "run")
        {
            var script = arguments[1];
            var scripted = $"{program} run {script}";
            if (script.Contains("bench", StringComparison.Ordinal))
            {
                return new(AgentWork.Benchmark, scripted);
            }
            if (script.Contains("test", StringComparison.Ordinal))
            {
                return new(AgentWork.Tests, scripted);
            }
            return script.Contains("build", StringComparison.Ordinal) ? new(AgentWork.Build, scripted) : new(AgentWork.Terminal, scripted);
        }
        if (named is not null && Subcommands.TryGetValue(program, out var table) && table.TryGetValue(named, out var work))
        {
            return new(work, detail);
        }
        if (ActsLate.Contains(program) && Subcommands.TryGetValue(program, out var late))
        {
            foreach (var argument in arguments)
            {
                if (late.TryGetValue(argument, out var action))
                {
                    return new(action, detail);
                }
            }
        }
        return new(Programs.GetValueOrDefault(program, AgentWork.Terminal), detail);
    }
}
