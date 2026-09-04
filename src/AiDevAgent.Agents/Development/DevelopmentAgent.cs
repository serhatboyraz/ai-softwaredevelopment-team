using AiDevAgent.Application.Agents;
using AiDevAgent.Application.Contracts;
using AiDevAgent.Agents.Support;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Agents.Development;

public sealed class DevelopmentAgent(
    IPromptStore prompts,
    IRepositoryTools repository,
    LlmAgentRunner llm,
    ILogger<DevelopmentAgent> logger) : IDevelopmentAgent
{
    public async Task<AgentOutcome<DevelopmentResult>> RunAsync(
        AgentWorkItem item,
        CancellationToken cancellationToken = default)
    {
        if (!llm.IsConfigured)
        {
            logger.LogInformation("Development Agent offline; skipping code edits.");
            var skipped = new DevelopmentResult(
                true,
                [],
                "No code changes (Azure OpenAI is not configured).",
                ["Offline mode"]);
            return new AgentOutcome<DevelopmentResult>(skipped, Summary: skipped.Summary);
        }

        var workspace = new WorkspaceToolContext { WorkspacePath = item.WorkspacePath };
        var tools = new RepositoryAiTools(repository, workspace, logger);
        var user = $"""
            Task title: {item.TaskTitle}
            Task description:
            {item.TaskDescription}

            Analysis JSON:
            {item.AnalysisJson ?? "(none)"}

            {(string.IsNullOrWhiteSpace(item.UserInstructions) ? "" : $"""
            Additional user commands (implement these now):
            {item.UserInstructions}

            """)}
            {(string.IsNullOrWhiteSpace(item.TestFailureSummary)
                ? "Implement the plan using workspace write/read tools only."
                : """
                The sandbox failed. Fix compiler/test errors, not NuGet vulnerability warnings.
                Ignore NU1903/warning lines unless there is no ": error CS" / ": error MSB" line.
                Do not bump Microsoft.OpenApi (or other packages) only to silence NU1903.
                Sandbox output:
                """ + item.TestFailureSummary)}

            Keep diffs small. You may also include a files array in the structured result.
            """;

        var (dto, promptTokens, completionTokens) = await llm.RunAsync<DevelopmentResultDto>(
            "Development",
            prompts.Get("development"),
            user,
            tools.DevelopmentTools(),
            cancellationToken);

        foreach (var file in dto.Files.Take(25))
        {
            if (string.IsNullOrWhiteSpace(file.Path)) continue;
            try
            {
                await repository.WriteFileAsync(item.WorkspacePath, file.Path, file.Content ?? string.Empty, cancellationToken);
                var normalized = file.Path.Replace('\\', '/');
                if (!workspace.ChangedFiles.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                    workspace.ChangedFiles.Add(normalized);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Development Agent failed to write {Path}", file.Path);
            }
        }

        var changed = workspace.ChangedFiles
            .Concat(dto.ChangedFiles)
            .Select(f => f.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var mapped = new DevelopmentResult(
            dto.Success,
            changed,
            string.IsNullOrWhiteSpace(dto.Summary) ? $"Modified {changed.Count} file(s)." : dto.Summary,
            dto.Notes);

        return new AgentOutcome<DevelopmentResult>(
            mapped,
            promptTokens,
            completionTokens,
            LlmAgentRunner.Truncate(mapped.Summary, 240));
    }
}
