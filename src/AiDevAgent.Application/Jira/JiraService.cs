using AiDevAgent.Application.DTOs;
using AiDevAgent.Application.Workflows;
using AiDevAgent.Domain.Entities;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DomainTaskStatus = AiDevAgent.Domain.Enums.TaskStatus;

namespace AiDevAgent.Application.Jira;

public sealed class JiraService(
    IJiraClient jira,
    IProjectRepository projects,
    IDevTaskRepository tasks,
    IUnitOfWork unitOfWork,
    WorkflowService workflows,
    IConfiguration configuration,
    ILogger<JiraService> logger)
{
    public JiraStatusDto GetStatus()
    {
        var baseUrl = configuration["Jira:BaseUrl"]?.Trim();
        var token = configuration["Jira:Token"];
        var email = configuration["Jira:Email"];
        var boardId = configuration.GetValue<int?>("Jira:BoardId");
        if (boardId is 0)
            boardId = null;

        var hasBase = !string.IsNullOrWhiteSpace(baseUrl);
        var hasToken = !string.IsNullOrWhiteSpace(token);
        var cloud = LooksLikeCloud(baseUrl);
        var needsEmail = cloud && string.IsNullOrWhiteSpace(email);

        if (!hasBase || !hasToken)
        {
            return new JiraStatusDto(
                false,
                hasBase ? baseUrl!.TrimEnd('/') : null,
                boardId,
                "Set Jira:BaseUrl and Jira:Token in appsettings.json. For Jira Cloud also set Jira:Email.");
        }

        if (needsEmail)
        {
            return new JiraStatusDto(
                false,
                baseUrl!.TrimEnd('/'),
                boardId,
                "Jira Cloud requires Jira:Email together with Jira:Token.");
        }

        return new JiraStatusDto(true, baseUrl!.TrimEnd('/'), boardId, null);
    }

    public async Task<IReadOnlyList<JiraBoardDto>> ListBoardsAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var boards = await jira.ListBoardsAsync(cancellationToken);
        return boards.Select(b => new JiraBoardDto(b.Id, b.Name, b.Type, b.ProjectKey)).ToList();
    }

    public async Task<JiraBoardWorkDto?> GetBoardWorkAsync(int boardId, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var board = await jira.GetBoardAsync(boardId, cancellationToken);
        if (board is null)
            return null;

        var sprints = await jira.ListActiveSprintsAsync(boardId, cancellationToken);
        var backlog = await jira.ListBacklogIssuesAsync(boardId, cancellationToken);
        var backlogKeys = backlog.Select(i => i.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

        JiraSprintWorkDto? active = null;
        IReadOnlyList<JiraIssueSummaryDto> boardIssues = [];

        if (sprints.Count > 0)
        {
            var sprint = sprints[0];
            var issues = await jira.ListSprintIssuesAsync(sprint.Id, cancellationToken);
            active = new JiraSprintWorkDto(sprint.Id, sprint.Name, issues.Select(ToSummaryDto).ToList());
        }
        else
        {
            var onBoard = await jira.ListBoardIssuesAsync(boardId, cancellationToken);
            boardIssues = onBoard
                .Where(i => !backlogKeys.Contains(i.Key))
                .Select(ToSummaryDto)
                .ToList();
        }

        return new JiraBoardWorkDto(
            board.Id,
            board.Name,
            board.Type,
            active,
            boardIssues,
            backlog.Select(ToSummaryDto).ToList());
    }

    public async Task<JiraIssueDetailsDto?> GetIssueAsync(string issueKey, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var issue = await jira.GetIssueAsync(issueKey, cancellationToken);
        return issue is null ? null : ToDetailsDto(issue);
    }

    public async Task<WorkflowDto?> StartWorkflowAsync(Guid projectId, string issueKey, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var project = await projects.GetByIdAsync(projectId, cancellationToken);
        if (project is null)
            return null;

        var key = issueKey.Trim();
        var issue = await jira.GetIssueAsync(key, cancellationToken)
            ?? throw new JiraApiException($"Jira issue '{key}' was not found.", 404);

        var task = await tasks.GetByProjectAndJiraKeyAsync(projectId, issue.Key, cancellationToken);
        if (task is null)
        {
            task = new DevTask
            {
                ProjectId = projectId,
                Title = JiraIssueFormatter.Title(issue),
                Description = JiraIssueFormatter.ToTaskDescription(issue),
                JiraIssueKey = issue.Key,
                JiraIssueUrl = issue.Url,
                Status = DomainTaskStatus.Draft
            };
            await tasks.AddAsync(task, cancellationToken);
        }
        else
        {
            task.Title = JiraIssueFormatter.Title(issue);
            task.Description = JiraIssueFormatter.ToTaskDescription(issue);
            task.JiraIssueUrl = issue.Url;
            task.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Starting workflow from Jira {IssueKey} for project {ProjectId}", issue.Key, projectId);
        return await workflows.StartAsync(new CreateWorkflowRequest(task.Id), cancellationToken);
    }

    private void EnsureConfigured()
    {
        var status = GetStatus();
        if (!status.Configured)
            throw new InvalidOperationException(status.Message ?? "Jira is not configured.");
    }

    private static bool LooksLikeCloud(string? baseUrl) =>
        !string.IsNullOrWhiteSpace(baseUrl)
        && baseUrl.Contains("atlassian.net", StringComparison.OrdinalIgnoreCase);

    private static JiraIssueSummaryDto ToSummaryDto(JiraIssueSummary issue) =>
        new(issue.Key, issue.Id, issue.Summary, issue.Status, issue.IssueType, issue.Priority, issue.Assignee);

    private static JiraIssueDetailsDto ToDetailsDto(JiraIssueDetails issue) =>
        new(
            issue.Key,
            issue.Id,
            issue.Summary,
            issue.Description,
            JiraIssueFormatter.ToTaskDescription(issue),
            issue.Status,
            issue.IssueType,
            issue.Priority,
            issue.Assignee,
            issue.Labels,
            issue.ParentKey,
            issue.ParentSummary,
            issue.Url,
            issue.Comments.Select(c => new JiraCommentDto(c.Author, c.Created, c.Body)).ToList());
}
