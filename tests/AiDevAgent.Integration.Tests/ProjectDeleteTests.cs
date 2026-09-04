using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AiDevAgent.Integration.Tests;

public class ProjectDeleteTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };

    public ProjectDeleteTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Delete_RemovesProjectAndRelatedTasks()
    {
        var projectRes = await _client.PostAsJsonAsync("/api/projects", new
        {
            name = "To delete",
            gitLabProjectId = "1",
            repositoryUrl = "https://example.invalid/org/demo.git"
        });
        Assert.Equal(HttpStatusCode.Created, projectRes.StatusCode);
        var project = await projectRes.Content.ReadFromJsonAsync<IdDto>(Json);
        Assert.NotNull(project);

        var taskRes = await _client.PostAsJsonAsync($"/api/projects/{project.Id}/tasks", new
        {
            title = "Task",
            description = "Will be removed with the project."
        });
        Assert.Equal(HttpStatusCode.Created, taskRes.StatusCode);

        var deleteRes = await _client.DeleteAsync($"/api/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        var getRes = await _client.GetAsync($"/api/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getRes.StatusCode);

        var missing = await _client.DeleteAsync($"/api/projects/{project.Id}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    private sealed class IdDto
    {
        public Guid Id { get; set; }
    }
}
