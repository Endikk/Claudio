using Claudio.Core.Models;

namespace Claudio.Core.Tests;

/// <summary>What a tool call is for, read from the call alone: its tool, its command, its file.</summary>
public sealed class AgentWorkTests
{
    private static AgentWork Work(string command) => AgentWorks.Read("Bash", command).Work;

    [Fact]
    public void ACommandIsNamedByItsProgram()
    {
        Assert.Equal(AgentWork.Tests, Work("swift test"));
        Assert.Equal(AgentWork.Tests, Work("pytest -q tests/"));
        Assert.Equal(AgentWork.Build, Work("xcodebuild -project Claudy.xcodeproj -scheme Claudy build"));
        Assert.Equal(AgentWork.Benchmark, Work("hyperfine './scan'"));
        Assert.Equal(AgentWork.Infra, Work("terraform plan -out tf.plan"));
        Assert.Equal(AgentWork.Infra, Work("kubectl get pods"));
        Assert.Equal(AgentWork.Security, Work("trivy fs ."));
        Assert.Equal(AgentWork.Data, Work("psql -c 'select 1'"));
        Assert.Equal(AgentWork.Git, Work("git commit -m wip"));
        Assert.Equal(AgentWork.Search, Work("rg TODO Claudy"));
        Assert.Equal(AgentWork.Web, Work("curl -s https://example.com"));
        Assert.Equal(AgentWork.Terminal, Work("./Scripts/build-app.sh"));
    }

    [Fact]
    public void TheFirstArgumentDecidesForProgramsThatDoSeveralThings()
    {
        Assert.Equal(AgentWork.Tests, Work("npm test"));
        Assert.Equal(AgentWork.Install, Work("npm install left-pad"));
        Assert.Equal(AgentWork.Security, Work("npm audit"));
        Assert.Equal(AgentWork.Build, Work("npm run build"));
        Assert.Equal(AgentWork.Tests, Work("pnpm run test:unit"));
        Assert.Equal(AgentWork.Benchmark, Work("cargo bench"));
        Assert.Equal(AgentWork.Benchmark, Work("go test -bench=. ./..."));
        Assert.Equal(AgentWork.Deploy, Work("docker push registry/app"));
        Assert.Equal(AgentWork.Infra, Work("docker compose up"));
        Assert.Equal(AgentWork.Deploy, Work("gh workflow run release.yml"));
    }

    [Fact]
    public void TheActionOfXcodebuildIsFoundAfterItsOptions()
    {
        Assert.Equal(AgentWork.Tests, Work("xcodebuild test -project Claudy.xcodeproj -scheme Claudy"));
        Assert.Equal(AgentWork.Tests, Work("xcodebuild -project Claudy.xcodeproj -scheme Claudy test"));
    }

    [Fact]
    public void ReadingGitIsReviewAndWritingItIsGit()
    {
        Assert.Equal(AgentWork.Review, Work("git diff main...HEAD"));
        Assert.Equal(AgentWork.Review, Work("gh pr view 12"));
        Assert.Equal(AgentWork.Git, Work("gh pr create --fill"));
        Assert.Equal(AgentWork.Git, Work("git push origin develop"));
    }

    [Fact]
    public void AChainedLineIsNamedByItsMostTellingCommand()
    {
        Assert.Equal(AgentWork.Tests, Work("cd app && npm test"));
        Assert.Equal(AgentWork.Build, Work("export CI=1; git status && cargo build | tail -5"));
        Assert.Equal(AgentWork.Tests, Work("FOO=bar sudo npx jest --watch"));
    }

    [Fact]
    public void WhatQuotesHoldNeitherSplitsNorNamesTheLine()
    {
        Assert.Equal(AgentWork.Git, Work("gh pr create --title \"run checks\""));
        Assert.Equal(AgentWork.Git, Work("git commit -m 'fix the log | swift test'"));
        Assert.Equal(AgentWork.Git, Work("git commit -m wip diff"));
    }

    [Fact]
    public void AFileNamedBenchIsNotABenchmark()
    {
        Assert.Equal(AgentWork.Search, Work("cat benchmarks.md"));
        Assert.Equal(AgentWork.Search, Work("grep -r benchmark ."));
        Assert.Equal("npm run test", AgentWorks.Read("Bash", "npm run test").Detail);
    }

    [Fact]
    public void TheDetailIsTheCommandsFirstWords()
    {
        Assert.Equal("swift test", AgentWorks.Read("Bash", "cd app && swift test --filter Scan").Detail);
        Assert.Equal("make", AgentWorks.Read("Bash", "/usr/bin/make -j8").Detail);
        Assert.Null(AgentWorks.Read("Bash", "cd app").Detail);
    }

    /// <summary>Windows adds a suffix to a program and a backslash to a path; neither changes the work.</summary>
    [Fact]
    public void AWindowsProgramIsNamedWithoutItsSuffix()
    {
        Assert.Equal(new WorkReading(AgentWork.Tests, "dotnet test"), AgentWorks.Read("Bash", @"C:\dotnet\dotnet.exe test tests"));
        Assert.Equal(new WorkReading(AgentWork.Install, "npm install"), AgentWorks.Read("Bash", "npm.cmd install"));
        Assert.Equal(AgentWork.Build, Work("msbuild Claudio.slnx"));
        Assert.Equal(AgentWork.Install, Work("winget install Git.Git"));
    }

    [Fact]
    public void AWrittenFileIsNamedByWhereItIsAndWhatItIs()
    {
        static AgentWork Written(string path) => AgentWorks.Read("Edit", path: path).Work;
        Assert.Equal(AgentWork.Code, Written("/repo/Claudy/Services/AgentScanner.swift"));
        Assert.Equal(AgentWork.Docs, Written("/repo/README.md"));
        Assert.Equal(AgentWork.Plan, Written("/repo/specs/001-room/spec.md"));
        Assert.Equal(AgentWork.Plan, Written("/repo/tasks.md"));
        Assert.Equal(AgentWork.Design, Written("/repo/web/styles/card.css"));
        Assert.Equal(AgentWork.Infra, Written("/repo/infra/main.tf"));
        Assert.Equal(AgentWork.Deploy, Written("/repo/.github/workflows/preflight.yml"));
        Assert.Equal(AgentWork.Data, Written("/repo/db/migrations/0004_add.sql"));
        Assert.Equal(AgentWork.Deploy, Written(@"C:\repo\.github\workflows\ci.yml"));
        Assert.Equal(AgentWork.Plan, Written(@"C:\repo\specs\001\plan.md"));
    }

    [Fact]
    public void OtherToolsAreNamedByWhatTheyAre()
    {
        Assert.Equal(AgentWork.Search, AgentWorks.Read("Grep").Work);
        Assert.Equal(AgentWork.Web, AgentWorks.Read("WebSearch").Work);
        Assert.Equal(AgentWork.Delegating, AgentWorks.Read("Agent").Work);
        Assert.Equal(AgentWork.Web, AgentWorks.Read("mcp__plugin_chrome-devtools__click").Work);
        Assert.Equal(AgentWork.Other, AgentWorks.Read("TodoWrite").Work);
    }

    [Fact]
    public void AnUnknownSkillOrServerShowsUnderItsOwnName()
    {
        Assert.Equal(new WorkReading(AgentWork.Skill, "speckit-plan"), AgentWorks.Read("Skill", skill: "speckit-plan"));
        Assert.Equal(new WorkReading(AgentWork.Mcp, "notion"), AgentWorks.Read("mcp__notion__search"));
    }

    [Fact]
    public void EveryWorkThatSaysWhereItIsDoneHasAFamily()
    {
        Assert.Equal(WorkFamily.Writing, AgentWork.Code.Family());
        Assert.Equal(WorkFamily.Commands, AgentWork.Tests.Family());
        Assert.Equal(WorkFamily.Research, AgentWork.Web.Family());
        Assert.Null(AgentWork.Delegating.Family());
        Assert.Null(AgentWork.Skill.Family());
    }

    // Where an agent sits

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_793_468_000);

    private static RunningAgent Agent(params (string Tool, string? Command, double SecondsAgo)[] calls)
    {
        var actions = calls.Select(call =>
        {
            var reading = AgentWorks.Read(call.Tool, call.Command);
            return new AgentAction(Now.AddSeconds(-call.SecondsAgo), AgentActionKinds.Of(call.Tool), call.Tool, null, reading.Work, reading.Detail);
        }).ToList();
        return new RunningAgent
        {
            Id = "a",
            SessionId = "s",
            SessionPid = 1,
            SessionName = "s",
            Title = "t",
            StartedAt = Now.AddSeconds(-600),
            LastActivity = Now,
            ActionCount = actions.Count,
            Recent = actions,
            LastAction = actions.LastOrDefault(),
        };
    }

    [Fact]
    public void OneCallDoesNotMoveAnAgentAcrossTheRoom()
    {
        var reader = Agent(("Read", null, 50), ("Grep", null, 40), ("Read", null, 30), ("Bash", "swift test", 5));
        Assert.Equal(WorkFamily.Research, reader.Family(Now));
        Assert.Equal("tests · swift test", reader.WorkLabel);
    }

    [Fact]
    public void TheLatestFamilyWinsATie() =>
        Assert.Equal(WorkFamily.Commands, Agent(("Read", null, 40), ("Bash", "swift test", 5)).Family(Now));

    [Fact]
    public void OldCallsNoLongerCount() =>
        Assert.Equal(WorkFamily.Commands, Agent(("Read", null, 300), ("Read", null, 280), ("Bash", "make", 10)).Family(Now));
}
