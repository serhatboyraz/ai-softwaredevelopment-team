using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Infrastructure.GitLab;

public sealed class GitLabOptions
{
    public const string SectionName = "GitLab";
    public string BaseUrl { get; set; } = "https://gitlab.com";
    public string Token { get; set; } = string.Empty;
}

public sealed class GitLabClient(
    HttpClient http,
    IConfiguration configuration,
    ISecretProvider secrets,
    ILogger<GitLabClient> logger) : IGitLabClient
{
    public async Task<GitLabProjectInfo?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthAsync(cancellationToken);
        var encoded = Uri.EscapeDataString(projectId);
        using var response = await http.GetAsync($"api/v4/projects/{encoded}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();
        var dto = await response.Content.ReadFromJsonAsync<ProjectDto>(cancellationToken);
        if (dto is null) return null;

        return new GitLabProjectInfo(
            dto.Id.ToString(),
            dto.Name ?? string.Empty,
            dto.HttpUrlToRepo ?? string.Empty,
            dto.DefaultBranch ?? "main");
    }

    public async Task<GitLabMergeRequestInfo> CreateMergeRequestAsync(
        string projectId,
        string sourceBranch,
        string targetBranch,
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        await EnsureAuthAsync(cancellationToken);
        var encoded = Uri.EscapeDataString(projectId);
        var payload = new
        {
            source_branch = sourceBranch,
            target_branch = targetBranch,
            title,
            description,
            remove_source_branch = false
        };

        using var response = await http.PostAsJsonAsync($"api/v4/projects/{encoded}/merge_requests", payload, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("GitLab create MR failed: {Status} {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"GitLab create MR failed: {response.StatusCode}");
        }

        var dto = System.Text.Json.JsonSerializer.Deserialize<MergeRequestDto>(body)
            ?? throw new InvalidOperationException("Empty GitLab MR response.");

        return new GitLabMergeRequestInfo(dto.Iid, dto.WebUrl ?? string.Empty, dto.Title ?? title, dto.State ?? "opened");
    }

    private async Task EnsureAuthAsync(CancellationToken cancellationToken)
    {
        if (http.DefaultRequestHeaders.Contains("PRIVATE-TOKEN"))
            return;

        var token = await secrets.GetSecretAsync("GitLab:Token", cancellationToken)
            ?? configuration["GitLab:Token"];

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("GitLab token is not configured. Set GitLab:Token or secret GitLab:Token.");

        http.DefaultRequestHeaders.Remove("PRIVATE-TOKEN");
        http.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token);
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    private sealed class ProjectDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("http_url_to_repo")] public string? HttpUrlToRepo { get; set; }
        [JsonPropertyName("default_branch")] public string? DefaultBranch { get; set; }
    }

    private sealed class MergeRequestDto
    {
        [JsonPropertyName("iid")] public long Iid { get; set; }
        [JsonPropertyName("web_url")] public string? WebUrl { get; set; }
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
    }
}
