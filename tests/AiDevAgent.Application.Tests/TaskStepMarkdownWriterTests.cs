using AiDevAgent.Application.Contracts;
using AiDevAgent.Application.Workflows;
using AiDevAgent.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiDevAgent.Application.Tests;

public class TaskStepMarkdownWriterTests
{
    [Fact]
    public async Task WriteAsync_WritesIntoWorkspaceAndMirrorRoot()
    {
        var mirror = Path.Combine(Path.GetTempPath(), "aidevagent-artifacts-" + Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(Path.GetTempPath(), "aidevagent-workspace-" + Guid.NewGuid().ToString("N"));
        try
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Artifacts:Root"] = mirror
            }).Build();

            var writer = new TaskStepMarkdownWriter(config, NullLogger<TaskStepMarkdownWriter>.Instance);

            await writer.WriteAsync(
                workspace,
                "Create an API DotNet",
                AgentType.Analysis,
                "plan ready",
                new AnalysisResult("summary", "none", ["a.cs"], ["step 1"], [], ["tests"]),
                CancellationToken.None);

            await writer.WriteAsync(
                workspace,
                "Create an API DotNet",
                AgentType.Development,
                "coded",
                new DevelopmentResult(true, ["a.cs"], "ok", []),
                CancellationToken.None);

            await writer.WriteAsync(
                workspace,
                "Create an API DotNet",
                AgentType.Testing,
                "passed",
                new TestResult(true, true, 1, 0, []),
                CancellationToken.None);

            var workspaceFolder = Path.Combine(workspace, "ai-tasks", "create-an-api-dotnet");
            Assert.True(File.Exists(Path.Combine(workspaceFolder, "analysis.md")));
            Assert.True(File.Exists(Path.Combine(workspaceFolder, "developer.md")));
            Assert.True(File.Exists(Path.Combine(workspaceFolder, "test.md")));

            var mirrorFolder = Path.Combine(mirror, "create-an-api-dotnet");
            Assert.True(File.Exists(Path.Combine(mirrorFolder, "analysis.md")));

            var analysis = await File.ReadAllTextAsync(Path.Combine(workspaceFolder, "analysis.md"));
            Assert.Contains("Create an API DotNet", analysis);
            Assert.Contains("plan ready", analysis);
            Assert.Contains("step 1", analysis);
        }
        finally
        {
            if (Directory.Exists(mirror))
                Directory.Delete(mirror, recursive: true);
            if (Directory.Exists(workspace))
                Directory.Delete(workspace, recursive: true);
        }
    }

    [Theory]
    [InlineData(AgentType.Context, "context.md")]
    [InlineData(AgentType.Analysis, "analysis.md")]
    [InlineData(AgentType.Development, "developer.md")]
    [InlineData(AgentType.Testing, "test.md")]
    [InlineData(AgentType.Git, "git.md")]
    public void StepFileName_MatchesExpected(AgentType type, string expected)
        => Assert.Equal(expected, TaskStepMarkdownWriter.StepFileName(type));
}
