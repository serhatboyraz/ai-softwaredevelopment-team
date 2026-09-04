using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AiDevAgent.Integration.Tests;

public class WorkflowSmokeTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public WorkflowSmokeTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Workflow:EnableSandboxSmoke", "false");
        }).CreateClient();
    }

    [Fact]
    public async Task OfflineAgents_WalkStateMachineToCompleted()
    {
        var projectRes = await _client.PostAsJsonAsync("/api/projects", new
        {
            name = "Smoke",
            gitLabProjectId = "1",
            repositoryUrl = "https://example.invalid/org/demo.git"
        });
        Assert.Equal(HttpStatusCode.Created, projectRes.StatusCode);
        var project = await projectRes.Content.ReadFromJsonAsync<IdDto>(Json);
        Assert.NotNull(project);

        var taskRes = await _client.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new
        {
            title = "Add health check",
            description = "Return HTTP 200 from GET /health with a JSON status payload."
        });
        Assert.Equal(HttpStatusCode.Created, taskRes.StatusCode);
        var task = await taskRes.Content.ReadFromJsonAsync<IdDto>(Json);
        Assert.NotNull(task);

        var wfRes = await _client.PostAsJsonAsync("/api/workflows", new { taskId = task.Id });
        Assert.Equal(HttpStatusCode.Accepted, wfRes.StatusCode);
        var workflow = await wfRes.Content.ReadFromJsonAsync<IdDto>(Json);
        Assert.NotNull(workflow);

        WorkflowDetailDto? detail = null;
        for (var i = 0; i < 40; i++)
        {
            var get = await _client.GetFromJsonAsync<WorkflowDetailDto>($"/api/workflows/{workflow.Id}", Json);
            detail = get;
            if (get?.Workflow.State is 12 or 10 or 11)
                break;
            await Task.Delay(250);
        }

        Assert.NotNull(detail);
        Assert.True(detail.Workflow.State == 12, $"Expected Completed (12), got {detail.Workflow.State}: {detail.Workflow.ErrorMessage}\n{string.Join('\n', detail.Events.Select(e => e.EventType + ": " + e.Message))}");
        Assert.NotNull(detail.MergeRequest);
        Assert.Contains(detail.Events, e => e.EventType == "workflow.completed");
    }

    private sealed class IdDto
    {
        public Guid Id { get; set; }
    }

    private sealed class WorkflowDetailDto
    {
        public WorkflowDto Workflow { get; set; } = null!;
        public List<EventDto> Events { get; set; } = [];
        public MergeRequestDto? MergeRequest { get; set; }
    }

    private sealed class WorkflowDto
    {
        public int State { get; set; }
        public string? ErrorMessage { get; set; }
    }

    private sealed class EventDto
    {
        public string EventType { get; set; } = "";
        public string? Message { get; set; }
    }

    private sealed class MergeRequestDto
    {
        public string Title { get; set; } = "";
    }
}
