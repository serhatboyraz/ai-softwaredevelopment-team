using System.Text.RegularExpressions;
using AiDevAgent.Application.Agents;
using AiDevAgent.Application.Contracts;
using AiDevAgent.Application.ProjectType;
using AiDevAgent.Application.Sandbox;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Agents.Testing;

public sealed class TestingAgent(
    IRepositoryTools repository,
    ISandboxExecutor sandbox,
    IConfiguration configuration,
    ILogger<TestingAgent> logger) : ITestingAgent
{
    public async Task<AgentOutcome<TestResult>> RunAsync(
        AgentWorkItem item,
        CancellationToken cancellationToken = default)
    {
        var files = await repository.ListFilesAsync(item.WorkspacePath, cancellationToken: cancellationToken);
        var profile = ProjectTypeDetector.Detect(files);
        var timeout = TimeSpan.FromMinutes(configuration.GetValue("Docker:TimeoutMinutes", 5));
        var smoke = configuration.GetValue("Workflow:EnableSandboxSmoke", true);

        logger.LogInformation("Testing Agent project type={Kind} build={Build} test={Test}",
            profile.Kind, profile.BuildCommand, profile.TestCommand);

        SandboxResult? build = null;
        SandboxResult? test = null;

        if (profile.BuildCommand is not null)
            build = await sandbox.RunAsync(item.WorkspacePath, profile.BuildCommand, timeout, cancellationToken);

        if (build is { Success: false })
            return FailFromSandbox(item.WorkspacePath, build, buildPassed: false, fallbackSummary: "Build failed in sandbox.");

        if (profile.TestCommand is not null)
            test = await sandbox.RunAsync(item.WorkspacePath, profile.TestCommand, timeout, cancellationToken);

        if (build is null && test is null)
        {
            if (!smoke)
            {
                var skipped = ToResult(true, true, 0, 0, []);
                return new AgentOutcome<TestResult>(skipped, Summary: "No build/test commands detected; skipped.");
            }

            var smokeResult = await sandbox.RunAsync(item.WorkspacePath, "echo sandbox-ok", TimeSpan.FromSeconds(60), cancellationToken);
            if (!smokeResult.Success)
                return FailFromSandbox(item.WorkspacePath, smokeResult, buildPassed: false, fallbackSummary: "Sandbox smoke command failed.");

            var mapped = ToResult(true, true, 1, 0, []);
            return new AgentOutcome<TestResult>(mapped, Summary: "Sandbox smoke command succeeded.");
        }

        var output = string.Join('\n', new[] { build?.StdOut, test?.StdOut, test?.StdErr, build?.StdErr }.Where(s => !string.IsNullOrWhiteSpace(s)));
        var (passed, failedCount) = ParseCounts(output);
        var success = test?.Success ?? build?.Success ?? false;
        if (test is not null && !success && failedCount == 0)
            failedCount = 1;
        if (success && passed == 0)
            passed = 1;

        if (!success && test is not null)
            return FailFromSandbox(item.WorkspacePath, test, buildPassed: build?.Success ?? true, fallbackSummary: $"Tests failed ({failedCount} failed).", passed, failedCount);

        var result = ToResult(success, build?.Success ?? success, passed, failedCount, []);
        return new AgentOutcome<TestResult>(result, Summary: $"Build/tests passed ({result.Passed} passed).");
    }

    private static AgentOutcome<TestResult> FailFromSandbox(
        string workspacePath,
        SandboxResult sandbox,
        bool buildPassed,
        string fallbackSummary,
        int passed = 0,
        int failed = 1)
    {
        var compilerErrors = SandboxLogExcerpt.ExtractCompilerErrors(sandbox.StdOut, sandbox.StdErr);
        var excerpt = SandboxLogExcerpt.ForAgent(sandbox.StdOut, sandbox.StdErr);
        var errorFiles = SandboxLogExcerpt.ExtractErrorFiles(workspacePath, sandbox.StdOut, sandbox.StdErr);
        var infrastructure = compilerErrors.Count == 0
            && SandboxFailureClassifier.IsPackageFeedUnavailable(sandbox.StdOut, sandbox.StdErr);

        IReadOnlyList<string> failures;
        if (infrastructure)
        {
            failures =
            [
                "Sandbox NuGet/package restore failed (NU1301): the container could not reach the package feed.",
                excerpt
            ];
        }
        else if (compilerErrors.Count > 0)
        {
            var header = errorFiles.Count == 0
                ? "Compiler errors (NU1903 and other warnings are not the build failure):"
                : "Read and fix these workspace-relative files: " + string.Join(", ", errorFiles);
            failures = new[] { header }.Concat(compilerErrors.Take(8)).ToArray();
        }
        else
        {
            failures =
            [
                fallbackSummary.EndsWith('.') ? fallbackSummary : fallbackSummary + ".",
                excerpt
            ];
        }

        var summary = infrastructure
            ? "Build failed: NuGet restore (NU1301) — sandbox could not reach the package feed."
            : SandboxLogExcerpt.Summary(fallbackSummary, compilerErrors);

        return new AgentOutcome<TestResult>(
            ToResult(false, buildPassed, passed, failed, failures, infrastructure),
            Summary: summary);
    }

    private static TestResult ToResult(
        bool success,
        bool buildPassed,
        int passed,
        int failed,
        IReadOnlyList<string> failures,
        bool infrastructureFailure = false)
        => new(success, buildPassed, passed, failed, failures, infrastructureFailure);

    internal static (int Passed, int Failed) ParseCounts(string output)
    {
        var passed = FirstInt(output, """Passed:\s+(\d+)""")
            ?? FirstInt(output, """(\d+)\s+passed""");
        var failed = FirstInt(output, """Failed:\s+(\d+)""")
            ?? FirstInt(output, """(\d+)\s+failed""");
        return (passed ?? 0, failed ?? 0);
    }

    private static int? FirstInt(string text, string pattern)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var n) ? n : null;
    }
}
