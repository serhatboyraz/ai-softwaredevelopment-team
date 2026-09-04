using AiDevAgent.Application.Agents;
using AiDevAgent.Application.Contracts;
using AiDevAgent.Agents.Support;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Agents.Context;

public sealed class ContextAgent(
    IPromptStore prompts,
    IRepositoryTools repository,
    LlmAgentRunner llm,
    ILogger<ContextAgent> logger) : IContextAgent
{
    public async Task<AgentOutcome<ContextResult>> RunAsync(
        AgentWorkItem item,
        CancellationToken cancellationToken = default)
    {
        var files = await repository.ListFilesAsync(item.WorkspacePath, cancellationToken: cancellationToken);
        var filePreview = string.Join('\n', files.Take(80));

        if (!llm.IsConfigured)
        {
            var ready = item.TaskDescription.Trim().Length >= 20
                || !string.IsNullOrWhiteSpace(item.UserInstructions);
            var result = new ContextResult(
                ready,
                ready ? 0.65 : 0.2,
                ready ? [] : ["Task description is too vague to start implementation."],
                files.Take(15).ToList(),
                ready ? [] : ["What should happen on success and failure?"]);
            logger.LogInformation("Context Agent offline: Ready={Ready}", ready);
            return new AgentOutcome<ContextResult>(result, Summary: ready
                ? "Repository context is sufficient (offline heuristic)."
                : "Waiting for more task information (offline heuristic).");
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
            {filePreview}

            Decide if implementation can begin. Inspect the repo with tools if needed.
            """;

        var (dto, promptTokens, completionTokens) = await llm.RunAsync<ContextResultDto>(
            "Context",
            prompts.Get("context"),
            user,
            tools.InspectionTools(),
            cancellationToken);

        var mapped = new ContextResult(
            dto.Ready,
            dto.Confidence,
            dto.MissingInformation,
            dto.RelevantFiles,
            dto.Questions);

        return new AgentOutcome<ContextResult>(
            mapped,
            promptTokens,
            completionTokens,
            mapped.Ready
                ? "Repository context is sufficient."
                : $"Missing information: {string.Join("; ", mapped.Questions.Take(3))}");
    }
}
