using AiDevAgent.Application.Jira;
using AiDevAgent.Domain.Interfaces;

namespace AiDevAgent.Application.Tests;

public class JiraIssueFormatterTests
{
    [Fact]
    public void Title_PrefixesIssueKey()
    {
        var issue = Sample("PROJ-12", "Add health check", "Return 200 from /health.");
        Assert.Equal("[PROJ-12] Add health check", JiraIssueFormatter.Title(issue));
    }

    [Fact]
    public void Description_IncludesMetadataAndBody()
    {
        var issue = Sample("PROJ-12", "Add health check", "Return 200 from /health.");
        var text = JiraIssueFormatter.ToTaskDescription(issue);

        Assert.Contains("Jira issue: PROJ-12", text);
        Assert.Contains("Type: Story", text);
        Assert.Contains("Status: To Do", text);
        Assert.Contains("Parent: PROJ-1 — Platform", text);
        Assert.Contains("Return 200 from /health.", text);
        Assert.Contains("Jane Doe", text);
        Assert.Contains("Please include a JSON payload.", text);
    }

    private static JiraIssueDetails Sample(string key, string summary, string description) =>
        new(
            key,
            "10001",
            summary,
            description,
            "To Do",
            "Story",
            "High",
            "Alex",
            ["api"],
            "PROJ-1",
            "Platform",
            $"https://example.atlassian.net/browse/{key}",
            [new JiraComment("Jane Doe", "2026-01-02", "Please include a JSON payload.")]);
}
