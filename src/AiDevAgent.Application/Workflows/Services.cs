using AiDevAgent.Application.DTOs;
using AiDevAgent.Application.Git;
using AiDevAgent.Domain.Entities;
using AiDevAgent.Domain.Enums;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DomainTaskStatus = AiDevAgent.Domain.Enums.TaskStatus;

namespace AiDevAgent.Application.Workflows;

public sealed class ProjectService(
    IProjectRepository projects,
    IUnitOfWork unitOfWork,
    IGitLabClient gitLab,
    IWorkspaceManager workspaces,
    ILogger<ProjectService> logger)
{
    public async Task<ProjectDto> CreateAsync(CreateProjectRequest request, CancellationToken cancellationToken = default)
    {
        var defaultBranch = string.IsNullOrWhiteSpace(request.DefaultBranch)
            ? await TryDetectDefaultBranchAsync(request, cancellationToken)
            : request.DefaultBranch.Trim();

        var project = new Project
        {
            Name = request.Name.Trim(),
            GitLabProjectId = request.GitLabProjectId.Trim(),
            RepositoryUrl = request.RepositoryUrl.Trim(),
            DefaultBranch = defaultBranch,
            Description = request.Description
        };

        await projects.AddAsync(project, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(project);
    }

    public async Task<IReadOnlyList<ProjectDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = await projects.ListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<ProjectDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(id, cancellationToken);
        return project is null ? null : ToDto(project);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workflowIds = await projects.RemoveAsync(id, cancellationToken);
        if (workflowIds is null) return false;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        foreach (var workflowId in workflowIds)
            await workspaces.CleanupAsync(workflowId, cancellationToken);

        logger.LogInformation("Deleted project {ProjectId} and {WorkflowCount} related workflow(s)", id, workflowIds.Count);
        return true;
    }

    private static ProjectDto ToDto(Project p) => new(
        p.Id, p.Name, p.GitLabProjectId, p.RepositoryUrl, p.DefaultBranch, p.Description, p.CreatedAt);

    private async Task<string?> TryDetectDefaultBranchAsync(CreateProjectRequest request, CancellationToken cancellationToken)
    {
        var projectId = request.GitLabProjectId.Trim();
        if (string.IsNullOrWhiteSpace(projectId) || projectId == "0")
            projectId = GitCloneUrl.ProjectPathFromUrl(request.RepositoryUrl) ?? string.Empty;

        if (string.IsNullOrWhiteSpace(projectId))
            return null;

        try
        {
            var info = await gitLab.GetProjectAsync(projectId, cancellationToken);
            if (!string.IsNullOrWhiteSpace(info?.DefaultBranch))
            {
                logger.LogInformation(
                    "GitLab default branch for {Project} is {Branch}",
                    projectId,
                    info.DefaultBranch);
                return info.DefaultBranch;
            }
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Could not detect default branch from GitLab for {Project}", projectId);
        }

        return null;
    }
}

public sealed class TaskService(
    IProjectRepository projects,
    IDevTaskRepository tasks,
    IUnitOfWork unitOfWork)
{
    public async Task<TaskDto?> CreateAsync(Guid projectId, CreateTaskRequest request, CancellationToken cancellationToken = default)
    {
        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null) return null;

        var task = new DevTask
        {
            ProjectId = projectId,
            Title = request.Title.Trim(),
            Description = request.Description.Trim(),
            Status = DomainTaskStatus.Draft
        };

        await tasks.AddAsync(task, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ToDto(task);
    }

    public async Task<IReadOnlyList<TaskDto>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var items = await tasks.ListByProjectAsync(projectId, cancellationToken);
        return items.Select(ToDto).ToList();
    }

    private static TaskDto ToDto(DevTask t) => new(
        t.Id, t.ProjectId, t.Title, t.Description, t.Status, t.CreatedAt, t.JiraIssueKey, t.JiraIssueUrl);
}

public sealed class WorkflowService(
    IDevTaskRepository tasks,
    IWorkflowRepository workflows,
    IWorkflowQueue queue,
    IUnitOfWork unitOfWork,
    IConfiguration configuration,
    ILogger<WorkflowService> logger)
{
    public async Task<WorkflowDto?> StartAsync(CreateWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        var task = await tasks.GetByIdAsync(request.TaskId, cancellationToken);
        if (task is null) return null;

        var workflow = new Workflow
        {
            TaskId = task.Id,
            State = WorkflowState.Pending,
            MaxRepairAttempts = configuration.GetValue("Workflow:MaxRepairAttempts", 3)
        };

        workflow.Events.Add(new WorkflowEvent
        {
            WorkflowId = workflow.Id,
            EventType = "workflow.created",
            Message = "Workflow created and queued."
        });

        task.Status = DomainTaskStatus.Queued;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        await workflows.AddAsync(workflow, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(workflow.Id, cancellationToken);

        logger.LogInformation("Queued workflow {WorkflowId} for task {TaskId}", workflow.Id, task.Id);
        return ToDto(workflow);
    }

    public async Task<IReadOnlyList<WorkflowDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var items = await workflows.ListAsync(cancellationToken);
        return items.Select(ToDto).ToList();
    }

    public async Task<WorkflowDetailDto?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workflow = await workflows.GetByIdWithDetailsAsync(id, cancellationToken);
        if (workflow is null) return null;

        return new WorkflowDetailDto(
            ToDto(workflow),
            workflow.Events.OrderBy(e => e.OccurredAt).Select(e => new WorkflowEventDto(e.Id, e.EventType, e.Message, e.OccurredAt)).ToList(),
            workflow.TestRuns.Select(t => new TestRunDto(t.Id, t.Success, t.BuildPassed, t.Passed, t.Failed, t.CompletedAt)).ToList(),
            WorkflowProgressMapper.Build(workflow),
            WorkflowProgressMapper.ToAgentRunDtos(workflow),
            workflow.MergeRequest is null
                ? null
                : new MergeRequestDto(
                    workflow.MergeRequest.Id,
                    workflow.MergeRequest.GitLabIid,
                    workflow.MergeRequest.Url,
                    workflow.MergeRequest.Title,
                    workflow.MergeRequest.State));
    }

    public async Task<bool> CancelAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workflow = await workflows.GetByIdAsync(id, cancellationToken);
        if (workflow is null) return false;
        if (workflow.State is WorkflowState.Completed or WorkflowState.Cancelled or WorkflowState.Failed)
            return false;

        workflow.State = WorkflowState.Cancelled;
        workflow.CompletedAt = DateTimeOffset.UtcNow;
        workflow.UpdatedAt = DateTimeOffset.UtcNow;
        await workflows.AddEventAsync(new WorkflowEvent
        {
            WorkflowId = workflow.Id,
            EventType = "workflow.cancelled",
            Message = "Workflow cancelled by user."
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ApproveAsync(Guid id, ApproveWorkflowRequest request, CancellationToken cancellationToken = default)
    {
        var workflow = await workflows.GetByIdWithDetailsAsync(id, cancellationToken);
        if (workflow is null) return false;
        if (workflow.State != WorkflowState.WaitingForApproval) return false;

        var approval = workflow.ApprovalRequests.LastOrDefault(a => a.Status == ApprovalStatus.Pending);
        if (approval is null) return false;

        approval.Status = request.Approved ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        approval.DecidedAt = DateTimeOffset.UtcNow;
        approval.Comment = request.Comment;
        approval.UpdatedAt = DateTimeOffset.UtcNow;

        if (request.Approved)
        {
            workflow.State = WorkflowState.ReadyForMr;
            await workflows.AddEventAsync(new WorkflowEvent
            {
                WorkflowId = workflow.Id,
                EventType = "human.approved",
                Message = "Human approved; continuing toward MR."
            }, cancellationToken);
            await queue.EnqueueAsync(workflow.Id, cancellationToken);
        }
        else
        {
            workflow.State = WorkflowState.Cancelled;
            workflow.CompletedAt = DateTimeOffset.UtcNow;
            await workflows.AddEventAsync(new WorkflowEvent
            {
                WorkflowId = workflow.Id,
                EventType = "human.rejected",
                Message = "Human rejected workflow."
            }, cancellationToken);
        }

        workflow.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<WorkflowRerunResult> RerunAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var workflow = await workflows.GetByIdAsync(id, cancellationToken);
        if (workflow is null)
            return WorkflowRerunResult.NotFound();

        if (workflow.State is WorkflowState.WaitingForApproval)
            return WorkflowRerunResult.Conflict("Approve or reject the pending request instead of re-running.");

        if (workflow.State is WorkflowState.Completed)
            return WorkflowRerunResult.Conflict("Completed workflows cannot be re-run.");

        var task = await tasks.GetByIdAsync(workflow.TaskId, cancellationToken);
        if (task is null)
            return WorkflowRerunResult.NotFound();

        var resumeState = ResolveResumeState(workflow);
        return await RequeueAsync(workflow, task, resumeState,
            "workflow.rerun",
            $"Workflow re-queued to continue from {resumeState}.",
            cancellationToken);
    }

    public async Task<WorkflowRerunResult> SendCommandAsync(Guid id, string command, CancellationToken cancellationToken = default)
    {
        var text = command.Trim();
        if (text.Length == 0)
            return WorkflowRerunResult.Conflict("Command is required.");
        if (text.Length > 4000)
            return WorkflowRerunResult.Conflict("Command is too long.");

        var workflow = await workflows.GetByIdAsync(id, cancellationToken);
        if (workflow is null)
            return WorkflowRerunResult.NotFound();

        var task = await tasks.GetByIdAsync(workflow.TaskId, cancellationToken);
        if (task is null)
            return WorkflowRerunResult.NotFound();

        await workflows.AddEventAsync(new WorkflowEvent
        {
            WorkflowId = workflow.Id,
            EventType = "human.command",
            Message = text
        }, cancellationToken);

        var resumeState = InferCheckpoint(workflow);
        return await RequeueAsync(workflow, task, resumeState,
            "workflow.rerun",
            $"New command queued; continuing development from {resumeState}.",
            cancellationToken);
    }

    private async Task<WorkflowRerunResult> RequeueAsync(
        Workflow workflow,
        DevTask task,
        WorkflowState resumeState,
        string eventType,
        string message,
        CancellationToken cancellationToken)
    {
        workflow.State = resumeState;
        workflow.CompletedAt = null;
        workflow.ErrorMessage = null;
        workflow.UpdatedAt = DateTimeOffset.UtcNow;
        if (workflow.RepairAttempt >= workflow.MaxRepairAttempts)
            workflow.RepairAttempt = 0;

        task.Status = DomainTaskStatus.Queued;
        task.UpdatedAt = DateTimeOffset.UtcNow;

        await workflows.AddEventAsync(new WorkflowEvent
        {
            WorkflowId = workflow.Id,
            EventType = eventType,
            Message = message
        }, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await queue.EnqueueAsync(workflow.Id, cancellationToken);

        logger.LogInformation("Re-queued workflow {WorkflowId} from {State}", workflow.Id, resumeState);
        return WorkflowRerunResult.Queued(ToDto(workflow));
    }

    private static WorkflowState ResolveResumeState(Workflow workflow) => workflow.State switch
    {
        WorkflowState.WaitingForInformation => WorkflowState.Analyzing,
        WorkflowState.MrCreated => WorkflowState.ReadyForMr,
        WorkflowState.Failed or WorkflowState.Cancelled => InferCheckpoint(workflow),
        _ => workflow.State
    };

    private static WorkflowState InferCheckpoint(Workflow workflow)
    {
        if (!string.IsNullOrWhiteSpace(workflow.AnalysisResultJson))
            return WorkflowState.Developing;
        if (!string.IsNullOrWhiteSpace(workflow.ContextResultJson))
            return WorkflowState.Analyzing;
        return WorkflowState.Pending;
    }

    private static WorkflowDto ToDto(Workflow w) => new(
        w.Id, w.TaskId, w.State, w.CorrelationId, w.RepairAttempt, w.MaxRepairAttempts,
        w.BranchName, w.CommitSha, w.ErrorMessage, w.CreatedAt, w.StartedAt, w.CompletedAt);
}
