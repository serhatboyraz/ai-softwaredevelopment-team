namespace AiDevAgent.Domain.Interfaces;

public interface IJiraClient
{
    Task<IReadOnlyList<JiraBoardInfo>> ListBoardsAsync(CancellationToken cancellationToken = default);
    Task<JiraBoardInfo?> GetBoardAsync(int boardId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraSprintInfo>> ListActiveSprintsAsync(int boardId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraIssueSummary>> ListSprintIssuesAsync(int sprintId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraIssueSummary>> ListBacklogIssuesAsync(int boardId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<JiraIssueSummary>> ListBoardIssuesAsync(int boardId, CancellationToken cancellationToken = default);
    Task<JiraIssueDetails?> GetIssueAsync(string issueKey, CancellationToken cancellationToken = default);
}

public sealed record JiraBoardInfo(int Id, string Name, string Type, string? ProjectKey);

public sealed record JiraSprintInfo(int Id, string Name, string State);

public sealed record JiraIssueSummary(
    string Key,
    string Id,
    string Summary,
    string Status,
    string IssueType,
    string? Priority,
    string? Assignee);

public sealed record JiraComment(string Author, string Created, string Body);

public sealed record JiraIssueDetails(
    string Key,
    string Id,
    string Summary,
    string Description,
    string Status,
    string IssueType,
    string? Priority,
    string? Assignee,
    IReadOnlyList<string> Labels,
    string? ParentKey,
    string? ParentSummary,
    string Url,
    IReadOnlyList<JiraComment> Comments);

public sealed class JiraApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
