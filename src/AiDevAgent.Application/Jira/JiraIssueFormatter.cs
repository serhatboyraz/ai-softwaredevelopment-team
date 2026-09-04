using System.Text;
using AiDevAgent.Domain.Interfaces;

namespace AiDevAgent.Application.Jira;

public static class JiraIssueFormatter
{
    public static string Title(JiraIssueDetails issue) =>
        string.IsNullOrWhiteSpace(issue.Summary)
            ? issue.Key
            : $"[{issue.Key}] {issue.Summary.Trim()}";

    public static string ToTaskDescription(JiraIssueDetails issue)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Jira issue: {issue.Key}");
        if (!string.IsNullOrWhiteSpace(issue.IssueType))
            builder.AppendLine($"Type: {issue.IssueType}");
        if (!string.IsNullOrWhiteSpace(issue.Status))
            builder.AppendLine($"Status: {issue.Status}");
        if (!string.IsNullOrWhiteSpace(issue.Priority))
            builder.AppendLine($"Priority: {issue.Priority}");
        if (!string.IsNullOrWhiteSpace(issue.Assignee))
            builder.AppendLine($"Assignee: {issue.Assignee}");
        if (issue.Labels.Count > 0)
            builder.AppendLine($"Labels: {string.Join(", ", issue.Labels)}");
        if (!string.IsNullOrWhiteSpace(issue.ParentKey))
        {
            var parent = string.IsNullOrWhiteSpace(issue.ParentSummary)
                ? issue.ParentKey
                : $"{issue.ParentKey} — {issue.ParentSummary}";
            builder.AppendLine($"Parent: {parent}");
        }

        if (!string.IsNullOrWhiteSpace(issue.Url))
            builder.AppendLine($"URL: {issue.Url}");

        builder.AppendLine();
        builder.AppendLine("Description");
        builder.AppendLine("-----------");
        builder.AppendLine(string.IsNullOrWhiteSpace(issue.Description)
            ? "(no description in Jira)"
            : issue.Description.Trim());

        if (issue.Comments.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("Comments");
            builder.AppendLine("--------");
            foreach (var comment in issue.Comments)
            {
                var when = string.IsNullOrWhiteSpace(comment.Created) ? "" : $" ({comment.Created})";
                builder.AppendLine($"{comment.Author}{when}:");
                builder.AppendLine(string.IsNullOrWhiteSpace(comment.Body) ? "(empty)" : comment.Body.Trim());
                builder.AppendLine();
            }
        }

        return builder.ToString().Trim();
    }
}
