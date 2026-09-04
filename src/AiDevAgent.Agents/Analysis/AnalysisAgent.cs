using AiDevAgent.Application.Agents;
using AiDevAgent.Application.Contracts;
using AiDevAgent.Agents.Support;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Agents.Analysis;

public sealed class AnalysisAgent(
    IPromptStore prompts,
    IRepositoryTools repository,
    LlmAgentRunner llm,
    ILogger<AnalysisAgent> logger) : IAnalysisAgent
{
    public async Task<AgentOutcome<AnalysisResult>> RunAsync(
        AgentWorkItem item,
        CancellationToken cancellationToken = default)
    {
        var files = await repository.ListFilesAsync(item.WorkspacePath, cancellationToken: cancellationToken);
        var preview = string.Join('\n', files.Take(80));

        if (!llm.IsConfigured)
        {
            var result = new AnalysisResult(
                $"Plan for: {item.TaskTitle}",
                "Unknown without a live model; keep the change localized.",
                files.Take(8).ToList(),
                ["Locate the relevant module.", "Implement the requested behavior.", "Cover with existing project tests."],
                ["AI is not configured; no code will be generated."],
                ["Run the detected project build and test commands in the sandbox."]);
            logger.LogInformation("Analysis Agent offline for {Title}", item.TaskTitle);
            return new AgentOutcome<AnalysisResult>(result, Summary: "Stub implementation plan prepared.");
        }

        var tools = new RepositoryAiTools(repository, new WorkspaceToolContext { WorkspacePath = item.WorkspacePath }, logger);
        var user = $"""
            Task title: {item.TaskTitle}
            Task description:
            {item.TaskDescription}

            {(string.IsNullOrWhiteSpace(item.UserInstructions) ? "" : $"""
            Additional user commands:
            {item.UserInstructions}

            """)}
            Workspace file preview:
            {preview}

            Produce an implementation plan. Inspect files with tools as needed.
            """;

        var (dto, promptTokens, completionTokens) = await llm.RunAsync<AnalysisResultDto>(
            "Analysis",
            prompts.Get("analysis"),
            user,
            tools.InspectionTools(),
            cancellationToken);

        var mapped = new AnalysisResult(
            dto.Summary,
            dto.ArchitectureImpact,
            dto.AffectedFiles,
            dto.ImplementationSteps,
            dto.Risks,
            dto.TestStrategy);

        return new AgentOutcome<AnalysisResult>(
            mapped,
            promptTokens,
            completionTokens,
            string.IsNullOrWhiteSpace(mapped.Summary)
                ? $"Identified {mapped.AffectedFiles.Count} affected files."
                : LlmAgentRunner.Truncate(mapped.Summary, 240));
    }
}
