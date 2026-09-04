using System.Text;
using System.Text.Json;
using AiDevAgent.Application.Contracts;
using AiDevAgent.Application.Agents;
using AiDevAgent.Domain.Enums;
using AiDevAgent.Domain.ValueObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Application.Workflows;

/// <summary>
/// Writes step results as markdown under <c>ai-tasks/{slug}/</c> in the workspace
/// (included in the Git commit) and mirrors them to the project-root <c>ai-tasks/</c> folder.
/// </summary>
public sealed class TaskStepMarkdownWriter(
    IConfiguration configuration,
    ILogger<TaskStepMarkdownWriter> logger) : ITaskStepArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task WriteAsync(
        string workspacePath,
        string taskTitle,
        AgentType agentType,
        string? summary,
        object result,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var slug = BranchName.NormalizeSlug(taskTitle);
            var fileName = StepFileName(agentType);
            var markdown = FormatMarkdown(taskTitle, agentType, summary, result);

            if (!string.IsNullOrWhiteSpace(workspacePath))
            {
                var workspaceFolder = Path.Combine(workspacePath, "ai-tasks", slug);
                await WriteFileAsync(workspaceFolder, fileName, markdown, cancellationToken);
            }

            var mirrorRoot = ResolveArtifactsRoot();
            var mirrorFolder = Path.Combine(mirrorRoot, slug);
            await WriteFileAsync(mirrorFolder, fileName, markdown, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write step artifact for {AgentType}", agentType);
        }
    }

    private async Task WriteFileAsync(
        string folder,
        string fileName,
        string markdown,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        await File.WriteAllTextAsync(path, markdown, Encoding.UTF8, cancellationToken);
        logger.LogInformation("Wrote step artifact {Path}", path);
    }

    private string ResolveArtifactsRoot()
    {
        var configured = configuration["Artifacts:Root"];
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        var projectRoot = FindProjectRoot(Directory.GetCurrentDirectory())
            ?? FindProjectRoot(AppContext.BaseDirectory);
        if (projectRoot is not null)
            return Path.Combine(projectRoot, "ai-tasks");

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "ai-tasks"));
    }

    private static string? FindProjectRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir is not null; dir = dir.Parent)
        {
            var apiProject = Path.Combine(dir.FullName, "src", "AiDevAgent.Api");
            if (Directory.Exists(apiProject))
                return dir.FullName;
        }

        return null;
    }

    public static string StepFileName(AgentType agentType) => agentType switch
    {
        AgentType.Context => "context.md",
        AgentType.Analysis => "analysis.md",
        AgentType.Development => "developer.md",
        AgentType.Testing => "test.md",
        AgentType.Git => "git.md",
        _ => $"{agentType.ToString().ToLowerInvariant()}.md"
    };

    private static string FormatMarkdown(string taskTitle, AgentType agentType, string? summary, object result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {agentType}");
        sb.AppendLine();
        sb.AppendLine($"**Task:** {taskTitle}");
        sb.AppendLine($"**Agent:** {agentType}");
        sb.AppendLine($"**Written:** {DateTimeOffset.UtcNow:O}");
        if (!string.IsNullOrWhiteSpace(summary))
        {
            sb.AppendLine();
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine(summary);
        }

        sb.AppendLine();
        AppendTypedSections(sb, result);

        sb.AppendLine();
        sb.AppendLine("## Raw result");
        sb.AppendLine();
        sb.AppendLine("```json");
        sb.AppendLine(JsonSerializer.Serialize(result, result.GetType(), JsonOptions));
        sb.AppendLine("```");
        sb.AppendLine();
        return sb.ToString();
    }

    private static void AppendTypedSections(StringBuilder sb, object result)
    {
        switch (result)
        {
            case ContextResult ctx:
                sb.AppendLine("## Context");
                sb.AppendLine();
                sb.AppendLine($"- **Ready:** {ctx.Ready}");
                sb.AppendLine($"- **Confidence:** {ctx.Confidence:0.##}");
                AppendList(sb, "Missing information", ctx.MissingInformation);
                AppendList(sb, "Relevant files", ctx.RelevantFiles);
                AppendList(sb, "Questions", ctx.Questions);
                break;

            case AnalysisResult analysis:
                sb.AppendLine("## Analysis");
                sb.AppendLine();
                sb.AppendLine(analysis.Summary);
                sb.AppendLine();
                sb.AppendLine("### Architecture impact");
                sb.AppendLine();
                sb.AppendLine(analysis.ArchitectureImpact);
                AppendList(sb, "Affected files", analysis.AffectedFiles);
                AppendList(sb, "Implementation steps", analysis.ImplementationSteps);
                AppendList(sb, "Risks", analysis.Risks);
                AppendList(sb, "Test strategy", analysis.TestStrategy);
                break;

            case DevelopmentResult development:
                sb.AppendLine("## Development");
                sb.AppendLine();
                sb.AppendLine($"- **Success:** {development.Success}");
                sb.AppendLine();
                sb.AppendLine(development.Summary);
                AppendList(sb, "Changed files", development.ChangedFiles);
                AppendList(sb, "Notes", development.Notes);
                break;

            case TestResult test:
                sb.AppendLine("## Test");
                sb.AppendLine();
                sb.AppendLine($"- **Success:** {test.Success}");
                sb.AppendLine($"- **Build passed:** {test.BuildPassed}");
                sb.AppendLine($"- **Passed:** {test.Passed}");
                sb.AppendLine($"- **Failed:** {test.Failed}");
                if (test.InfrastructureFailure)
                    sb.AppendLine("- **Infrastructure failure:** True");
                AppendList(sb, "Failures", test.Failures);
                break;

            case GitExecutionResult git:
                sb.AppendLine("## Git");
                sb.AppendLine();
                sb.AppendLine($"- **Success:** {git.Success}");
                sb.AppendLine($"- **Branch:** `{git.Branch}`");
                sb.AppendLine($"- **Commit:** `{git.CommitSha}`");
                if (!string.IsNullOrWhiteSpace(git.MergeRequestUrl))
                    sb.AppendLine($"- **Merge request:** {git.MergeRequestUrl}");
                sb.AppendLine($"- **Title:** {git.Title}");
                if (!string.IsNullOrWhiteSpace(git.Description))
                {
                    sb.AppendLine();
                    sb.AppendLine("### Description");
                    sb.AppendLine();
                    sb.AppendLine(git.Description);
                }
                break;
        }
    }

    private static void AppendList(StringBuilder sb, string heading, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"### {heading}");
        sb.AppendLine();
        foreach (var item in items)
            sb.AppendLine($"- {item}");
    }
}
