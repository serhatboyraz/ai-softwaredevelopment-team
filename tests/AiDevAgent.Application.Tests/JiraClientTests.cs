using System.Net;
using System.Text;
using System.Text.Json;
using AiDevAgent.Infrastructure.Jira;
using AiDevAgent.Infrastructure.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiDevAgent.Application.Tests;

public class JiraClientTests
{
    [Fact]
    public void Adf_ExtractsParagraphAndListText()
    {
        using var doc = JsonDocument.Parse(
            """
            {
              "type": "doc",
              "content": [
                { "type": "paragraph", "content": [{ "type": "text", "text": "Return 200." }] },
                {
                  "type": "bulletList",
                  "content": [
                    { "type": "listItem", "content": [{ "type": "paragraph", "content": [{ "type": "text", "text": "JSON body" }] }] }
                  ]
                }
              ]
            }
            """);

        var text = JiraAdfText.From(doc.RootElement);
        Assert.Contains("Return 200.", text);
        Assert.Contains("JSON body", text);
    }

    [Fact]
    public async Task Client_MapsBoardWorkAndIssueDetails()
    {
        var handler = new ScriptedHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.EndsWith("/rest/agile/1.0/board/7", StringComparison.Ordinal))
                return Json("""{"id":7,"name":"Team board","type":"scrum","location":{"projectKey":"PROJ"}}""");
            if (path.Contains("/sprint", StringComparison.Ordinal) && path.Contains("/board/7", StringComparison.Ordinal))
                return Json("""{"values":[{"id":3,"name":"Sprint 9","state":"active"}]}""");
            if (path.Contains("/sprint/3/issue", StringComparison.Ordinal))
                return Json("""{"total":1,"issues":[{"id":"1","key":"PROJ-10","fields":{"summary":"Do it","status":{"name":"To Do"},"issuetype":{"name":"Story"}}}]}""");
            if (path.Contains("/backlog", StringComparison.Ordinal))
                return Json("""{"total":1,"issues":[{"id":"2","key":"PROJ-11","fields":{"summary":"Later","status":{"name":"To Do"},"issuetype":{"name":"Task"}}}]}""");
            if (path.Contains("/rest/api/3/issue/PROJ-10", StringComparison.Ordinal))
            {
                return Json(
                    """
                    {
                      "id": "1",
                      "key": "PROJ-10",
                      "fields": {
                        "summary": "Do it",
                        "description": { "type": "doc", "content": [{ "type": "paragraph", "content": [{ "type": "text", "text": "Implement the endpoint." }] }] },
                        "status": { "name": "To Do" },
                        "issuetype": { "name": "Story" },
                        "priority": { "name": "High" },
                        "assignee": { "displayName": "Alex" },
                        "labels": ["api"],
                        "comment": { "comments": [] }
                      }
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jira:BaseUrl"] = "https://example.atlassian.net",
            ["Jira:Email"] = "dev@example.com",
            ["Jira:Token"] = "token"
        }).Build();

        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.atlassian.net/") };
        var client = new JiraClient(http, config, new ConfigurationSecretProvider(config), NullLogger<JiraClient>.Instance);

        var board = await client.GetBoardAsync(7);
        Assert.Equal("Team board", board?.Name);

        var sprints = await client.ListActiveSprintsAsync(7);
        Assert.Equal("Sprint 9", Assert.Single(sprints).Name);

        var sprintIssues = await client.ListSprintIssuesAsync(3);
        Assert.Equal("PROJ-10", Assert.Single(sprintIssues).Key);

        var backlog = await client.ListBacklogIssuesAsync(7);
        Assert.Equal("PROJ-11", Assert.Single(backlog).Key);

        var issue = await client.GetIssueAsync("PROJ-10");
        Assert.NotNull(issue);
        Assert.Equal("Do it", issue.Summary);
        Assert.Contains("Implement the endpoint.", issue.Description);
        Assert.Contains("/browse/PROJ-10", issue.Url);
        Assert.Contains("Basic", handler.Authorization ?? "", StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };

    private sealed class ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public string? Authorization { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(respond(request));
        }
    }
}
