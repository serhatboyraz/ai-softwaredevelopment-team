using AiDevAgent.Domain.Enums;

namespace AiDevAgent.Application.Workflows;

/// <summary>
/// Persists each agent step result under <c>ai-tasks/{task-slug}/{step}.md</c>
/// in the workflow workspace (so Git commits them) and mirrors to the project root.
/// </summary>
public interface ITaskStepArtifactWriter
{
    Task WriteAsync(
        string workspacePath,
        string taskTitle,
        AgentType agentType,
        string? summary,
        object result,
        CancellationToken cancellationToken = default);
}
