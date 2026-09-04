using AiDevAgent.Domain.Entities;

namespace AiDevAgent.Domain.Interfaces;

public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IProjectRepository
{
    Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Project project, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Guid>?> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

public interface IDevTaskRepository
{
    Task<DevTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<DevTask?> GetByProjectAndJiraKeyAsync(Guid projectId, string jiraIssueKey, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<DevTask>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default);
    Task AddAsync(DevTask task, CancellationToken cancellationToken = default);
}

public interface IWorkflowRepository
{
    Task<Workflow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Workflow?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Workflow>> ListAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Workflow>> ListPendingAsync(CancellationToken cancellationToken = default);
    Task AddAsync(Workflow workflow, CancellationToken cancellationToken = default);
    Task AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default);
    Task AddMergeRequestAsync(MergeRequest mergeRequest, CancellationToken cancellationToken = default);
    Task AddAgentRunAsync(AgentRun agentRun, CancellationToken cancellationToken = default);
    Task AddTestRunAsync(TestRun testRun, CancellationToken cancellationToken = default);
    Task AddApprovalRequestAsync(ApprovalRequest approvalRequest, CancellationToken cancellationToken = default);
    Task<bool> IsCancelledAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> ListUserCommandsAsync(Guid workflowId, CancellationToken cancellationToken = default);
}

public interface IWorkflowQueue
{
    ValueTask EnqueueAsync(Guid workflowId, CancellationToken cancellationToken = default);
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);
}

public interface IWorkflowEventPublisher
{
    Task PublishAsync(Guid workflowId, string eventType, string? message = null, object? payload = null, CancellationToken cancellationToken = default);
}

public interface IWorkflowExecutor
{
    Task ExecuteAsync(Guid workflowId, CancellationToken cancellationToken = default);
}
