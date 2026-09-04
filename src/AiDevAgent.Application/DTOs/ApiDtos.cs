using AiDevAgent.Domain.Enums;
using DomainTaskStatus = AiDevAgent.Domain.Enums.TaskStatus;

namespace AiDevAgent.Application.DTOs;

public sealed record CreateProjectRequest(
    string Name,
    string GitLabProjectId,
    string RepositoryUrl,
    string? DefaultBranch = null,
    string? Description = null);

public sealed record ProjectDto(
    Guid Id,
    string Name,
    string GitLabProjectId,
    string RepositoryUrl,
    string? DefaultBranch,
    string? Description,
    DateTimeOffset CreatedAt);

public sealed record CreateTaskRequest(
    string Title,
    string Description);

public sealed record TaskDto(
    Guid Id,
    Guid ProjectId,
    string Title,
    string Description,
    DomainTaskStatus Status,
    DateTimeOffset CreatedAt,
    string? JiraIssueKey = null,
    string? JiraIssueUrl = null);

public sealed record CreateWorkflowRequest(Guid TaskId);

public sealed record StartProjectWorkflowRequest(
    string? Title = null,
    string? Description = null,
    string? JiraIssueKey = null);

public sealed record JiraStatusDto(
    bool Configured,
    string? BaseUrl,
    int? DefaultBoardId,
    string? Message);

public sealed record JiraBoardDto(
    int Id,
    string Name,
    string Type,
    string? ProjectKey);

public sealed record JiraIssueSummaryDto(
    string Key,
    string Id,
    string Summary,
    string Status,
    string IssueType,
    string? Priority,
    string? Assignee);

public sealed record JiraCommentDto(
    string Author,
    string Created,
    string Body);

public sealed record JiraIssueDetailsDto(
    string Key,
    string Id,
    string Summary,
    string Description,
    string FormattedDescription,
    string Status,
    string IssueType,
    string? Priority,
    string? Assignee,
    IReadOnlyList<string> Labels,
    string? ParentKey,
    string? ParentSummary,
    string Url,
    IReadOnlyList<JiraCommentDto> Comments);

public sealed record JiraSprintWorkDto(
    int Id,
    string Name,
    IReadOnlyList<JiraIssueSummaryDto> Issues);

public sealed record JiraBoardWorkDto(
    int BoardId,
    string Name,
    string Type,
    JiraSprintWorkDto? ActiveSprint,
    IReadOnlyList<JiraIssueSummaryDto> BoardIssues,
    IReadOnlyList<JiraIssueSummaryDto> Backlog);

public sealed record WorkflowDto(
    Guid Id,
    Guid TaskId,
    WorkflowState State,
    string CorrelationId,
    int RepairAttempt,
    int MaxRepairAttempts,
    string? BranchName,
    string? CommitSha,
    string? ErrorMessage,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record WorkflowDetailDto(
    WorkflowDto Workflow,
    IReadOnlyList<WorkflowEventDto> Events,
    IReadOnlyList<TestRunDto> TestRuns,
    IReadOnlyList<WorkflowStepProgressDto> Steps,
    IReadOnlyList<AgentRunDto> AgentRuns,
    MergeRequestDto? MergeRequest);

public sealed record WorkflowStepProgressDto(
    string Key,
    string Label,
    string Status,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    long DurationMs,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string? Summary);

public sealed record AgentRunDto(
    Guid Id,
    AgentType AgentType,
    string? OutputSummary,
    bool Success,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int? PromptTokens,
    int? CompletionTokens);

public sealed record WorkflowEventDto(
    Guid Id,
    string EventType,
    string? Message,
    DateTimeOffset OccurredAt);

public sealed record TestRunDto(
    Guid Id,
    bool Success,
    bool BuildPassed,
    int Passed,
    int Failed,
    DateTimeOffset? CompletedAt);

public sealed record MergeRequestDto(
    Guid Id,
    long? GitLabIid,
    string? Url,
    string Title,
    string State);

public sealed record ApproveWorkflowRequest(bool Approved, string? Comment = null);

public sealed record SendWorkflowCommandRequest(string Command);

public sealed record WorkflowRerunResult(bool Found, bool Accepted, string? Message, WorkflowDto? Workflow)
{
    public static WorkflowRerunResult NotFound() => new(false, false, null, null);
    public static WorkflowRerunResult Conflict(string message) => new(true, false, message, null);
    public static WorkflowRerunResult Queued(WorkflowDto workflow) => new(true, true, null, workflow);
}
