using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AiDevAgent.Integration.Tests;

public class JiraIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _api;

    public JiraIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _api = factory.CreateClient();
    }

    [Fact]
    public async Task Status_Unconfigured_ByDefault()
    {
        var response = await _api.GetFromJsonAsync<StatusDto>("/api/jira/status");
        Assert.NotNull(response);
        Assert.False(response.Configured);
        Assert.False(string.IsNullOrWhiteSpace(response.Message));
    }

    private sealed class StatusDto
    {
        public bool Configured { get; set; }
        public string? Message { get; set; }
    }
}
