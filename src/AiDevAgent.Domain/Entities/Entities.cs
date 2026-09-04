namespace AiDevAgent.Domain.Entities;

public abstract class EntityBase
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class Project : EntityBase
{
    public string Name { get; set; } = string.Empty;
    public string GitLabProjectId { get; set; } = string.Empty;
    public string RepositoryUrl { get; set; } = string.Empty;
    public string? DefaultBranch { get; set; }
    public string? Description { get; set; }

    public ICollection<DevTask> Tasks { get; set; } = new List<DevTask>();
}

/// <summary>Named DevTask to avoid clash with System.Threading.Tasks.Task.</summary>
public sealed class DevTask : EntityBase
{
    public Guid ProjectId { get; set; }
    public Project Project { get; set; } = null!;

    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? JiraIssueKey { get; set; }
    public string? JiraIssueUrl { get; set; }
    public Enums.TaskStatus Status { get; set; } = Enums.TaskStatus.Draft;

    public ICollection<Workflow> Workflows { get; set; } = new List<Workflow>();
}

public sealed class Workflow : EntityBase
{
    public Guid TaskId { get; set; }
    public DevTask Task { get; set; } = null!;

    public Enums.WorkflowState State { get; set; } = Enums.WorkflowState.Pending;
    public string CorrelationId { get; set; } = Guid.NewGuid().ToString("N");
    public int RepairAttempt { get; set; }
    public int MaxRepairAttempts { get; set; } = 3;

    public string? BranchName { get; set; }
    public string? CommitSha { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ContextResultJson { get; set; }
    public string? AnalysisResultJson { get; set; }
    public string? WorkspacePath { get; set; }

    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }

    public ICollection<WorkflowStep> Steps { get; set; } = new List<WorkflowStep>();
    public ICollection<WorkflowEvent> Events { get; set; } = new List<WorkflowEvent>();
    public ICollection<AgentRun> AgentRuns { get; set; } = new List<AgentRun>();
    public ICollection<ToolCall> ToolCalls { get; set; } = new List<ToolCall>();
    public ICollection<TestRun> TestRuns { get; set; } = new List<TestRun>();
    public ICollection<Artifact> Artifacts { get; set; } = new List<Artifact>();
    public MergeRequest? MergeRequest { get; set; }
    public ICollection<ApprovalRequest> ApprovalRequests { get; set; } = new List<ApprovalRequest>();
}

public sealed class WorkflowStep : EntityBase
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    public string Name { get; set; } = string.Empty;
    public Enums.WorkflowStepState State { get; set; } = Enums.WorkflowStepState.Pending;
    public int Sequence { get; set; }
    public string? PayloadJson { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class WorkflowEvent : EntityBase
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    public string EventType { get; set; } = string.Empty;
    public string? Message { get; set; }
    public string? PayloadJson { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class AgentRun : EntityBase
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    public Enums.AgentType AgentType { get; set; }
    public string? InputSummary { get; set; }
    public string? OutputSummary { get; set; }
    public string? OutputJson { get; set; }
    public bool Success { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
}

public sealed class ToolCall : EntityBase
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;
    public Guid? AgentRunId { get; set; }

    public string ToolName { get; set; } = string.Empty;
    public string? ArgumentsSummary { get; set; }
    public string? ResultSummary { get; set; }
    public bool Success { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class TestRun : EntityBase
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    public bool BuildPassed { get; set; }
    public bool Success { get; set; }
    public int Passed { get; set; }
    public int Failed { get; set; }
    public string? FailuresJson { get; set; }
    public string? LogSummary { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

public sealed class Artifact : EntityBase
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    public string Kind { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? MetadataJson { get; set; }
}

public sealed class MergeRequest : EntityBase
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    public long? GitLabIid { get; set; }
    public string? Url { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string State { get; set; } = "opened";
}

public sealed class ApprovalRequest : EntityBase
{
    public Guid WorkflowId { get; set; }
    public Workflow Workflow { get; set; } = null!;

    public Enums.ApprovalStatus Status { get; set; } = Enums.ApprovalStatus.Pending;
    public string Reason { get; set; } = string.Empty;
    public string? DecidedBy { get; set; }
    public DateTimeOffset? DecidedAt { get; set; }
    public string? Comment { get; set; }
}
