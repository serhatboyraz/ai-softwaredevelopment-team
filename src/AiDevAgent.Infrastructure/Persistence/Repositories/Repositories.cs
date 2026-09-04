using AiDevAgent.Domain.Entities;
using AiDevAgent.Domain.Enums;
using AiDevAgent.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace AiDevAgent.Infrastructure.Persistence.Repositories;

public sealed class UnitOfWork(AppDbContext db) : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => db.SaveChangesAsync(cancellationToken);
}

public sealed class ProjectRepository(AppDbContext db) : IProjectRepository
{
    public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Projects.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
        => await db.Projects.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

    public async Task AddAsync(Project project, CancellationToken cancellationToken = default)
        => await db.Projects.AddAsync(project, cancellationToken);

    public async Task<IReadOnlyList<Guid>?> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await db.Projects.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (project is null) return null;

        var taskIds = await db.Tasks.Where(t => t.ProjectId == id).Select(t => t.Id).ToListAsync(cancellationToken);
        var workflows = await db.Workflows.Where(w => taskIds.Contains(w.TaskId)).ToListAsync(cancellationToken);
        var workflowIds = workflows.Select(w => w.Id).ToList();

        if (workflowIds.Count > 0)
        {
            db.WorkflowEvents.RemoveRange(await db.WorkflowEvents.Where(e => workflowIds.Contains(e.WorkflowId)).ToListAsync(cancellationToken));
            db.WorkflowSteps.RemoveRange(await db.WorkflowSteps.Where(e => workflowIds.Contains(e.WorkflowId)).ToListAsync(cancellationToken));
            db.AgentRuns.RemoveRange(await db.AgentRuns.Where(e => workflowIds.Contains(e.WorkflowId)).ToListAsync(cancellationToken));
            db.ToolCalls.RemoveRange(await db.ToolCalls.Where(e => workflowIds.Contains(e.WorkflowId)).ToListAsync(cancellationToken));
            db.TestRuns.RemoveRange(await db.TestRuns.Where(e => workflowIds.Contains(e.WorkflowId)).ToListAsync(cancellationToken));
            db.Artifacts.RemoveRange(await db.Artifacts.Where(e => workflowIds.Contains(e.WorkflowId)).ToListAsync(cancellationToken));
            db.MergeRequests.RemoveRange(await db.MergeRequests.Where(e => workflowIds.Contains(e.WorkflowId)).ToListAsync(cancellationToken));
            db.ApprovalRequests.RemoveRange(await db.ApprovalRequests.Where(e => workflowIds.Contains(e.WorkflowId)).ToListAsync(cancellationToken));
            db.Workflows.RemoveRange(workflows);
        }

        if (taskIds.Count > 0)
            db.Tasks.RemoveRange(await db.Tasks.Where(t => t.ProjectId == id).ToListAsync(cancellationToken));

        db.Projects.Remove(project);
        return workflowIds;
    }
}

public sealed class DevTaskRepository(AppDbContext db) : IDevTaskRepository
{
    public Task<DevTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Tasks.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<DevTask?> GetByProjectAndJiraKeyAsync(Guid projectId, string jiraIssueKey, CancellationToken cancellationToken = default)
        => db.Tasks.FirstOrDefaultAsync(
            x => x.ProjectId == projectId && x.JiraIssueKey == jiraIssueKey,
            cancellationToken);

    public async Task<IReadOnlyList<DevTask>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
        => await db.Tasks.Where(x => x.ProjectId == projectId).OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

    public async Task AddAsync(DevTask task, CancellationToken cancellationToken = default)
        => await db.Tasks.AddAsync(task, cancellationToken);
}

public sealed class WorkflowRepository(AppDbContext db) : IWorkflowRepository
{
    public Task<Workflow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Workflows.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<Workflow?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Workflows
            .AsSplitQuery()
            .Include(x => x.Events)
            .Include(x => x.TestRuns)
            .Include(x => x.AgentRuns)
            .Include(x => x.MergeRequest)
            .Include(x => x.ApprovalRequests)
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Workflow>> ListAsync(CancellationToken cancellationToken = default)
        => await db.Workflows.OrderByDescending(x => x.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Workflow>> ListPendingAsync(CancellationToken cancellationToken = default)
        => await db.Workflows
            .Where(x => x.State == WorkflowState.Pending)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Workflow workflow, CancellationToken cancellationToken = default)
        => await db.Workflows.AddAsync(workflow, cancellationToken);

    public async Task AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default)
        => await db.WorkflowEvents.AddAsync(workflowEvent, cancellationToken);

    public async Task AddMergeRequestAsync(MergeRequest mergeRequest, CancellationToken cancellationToken = default)
        => await db.MergeRequests.AddAsync(mergeRequest, cancellationToken);

    public async Task AddAgentRunAsync(AgentRun agentRun, CancellationToken cancellationToken = default)
        => await db.AgentRuns.AddAsync(agentRun, cancellationToken);

    public async Task AddTestRunAsync(TestRun testRun, CancellationToken cancellationToken = default)
        => await db.TestRuns.AddAsync(testRun, cancellationToken);

    public async Task AddApprovalRequestAsync(ApprovalRequest approvalRequest, CancellationToken cancellationToken = default)
        => await db.ApprovalRequests.AddAsync(approvalRequest, cancellationToken);

    public Task<bool> IsCancelledAsync(Guid id, CancellationToken cancellationToken = default)
        => db.Workflows.AsNoTracking()
            .AnyAsync(x => x.Id == id && x.State == WorkflowState.Cancelled, cancellationToken);

    public async Task<IReadOnlyList<string>> ListUserCommandsAsync(Guid workflowId, CancellationToken cancellationToken = default)
        => await db.WorkflowEvents.AsNoTracking()
            .Where(e => e.WorkflowId == workflowId && e.EventType == "human.command" && e.Message != null)
            .OrderBy(e => e.OccurredAt)
            .Select(e => e.Message!)
            .ToListAsync(cancellationToken);
}
