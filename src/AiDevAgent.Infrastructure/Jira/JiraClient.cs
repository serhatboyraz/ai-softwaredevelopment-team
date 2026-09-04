using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Infrastructure.Jira;

public sealed class JiraOptions
{
    public const string SectionName = "Jira";
    public string BaseUrl { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public int? BoardId { get; set; }
    public string? ProjectKey { get; set; }
}

public sealed class JiraClient(
    HttpClient http,
    IConfiguration configuration,
    ISecretProvider secrets,
    ILogger<JiraClient> logger) : IJiraClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public async Task<IReadOnlyList<JiraBoardInfo>> ListBoardsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureAuthAsync(cancellationToken);
        var path = "rest/agile/1.0/board?maxResults=50";
        var projectKey = configuration["Jira:ProjectKey"];
        if (!string.IsNullOrWhiteSpace(projectKey))
            path += "&projectKeyOrId=" + Uri.EscapeDataString(projectKey.Trim());

        var page = await GetJsonAsync<BoardPageDto>(path, cancellationToken);
        return (page?.Values ?? []).Select(MapBoard).ToList();
    }

    public async Task<JiraBoardInfo?> GetBoardAsync(int boardId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthAsync(cancellationToken);
        try
        {
            var dto = await GetJsonAsync<BoardDto>($"rest/agile/1.0/board/{boardId}", cancellationToken);
            return dto is null ? null : MapBoard(dto);
        }
        catch (JiraApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<JiraSprintInfo>> ListActiveSprintsAsync(int boardId, CancellationToken cancellationToken = default)
    {
        await EnsureAuthAsync(cancellationToken);
        try
        {
            var page = await GetJsonAsync<SprintPageDto>(
                $"rest/agile/1.0/board/{boardId}/sprint?state=active&maxResults=20",
                cancellationToken);
            return (page?.Values ?? [])
                .Select(s => new JiraSprintInfo(s.Id, s.Name ?? $"Sprint {s.Id}", s.State ?? "active"))
                .ToList();
        }
        catch (JiraApiException ex) when (ex.StatusCode is 400 or 404)
        {
            return [];
        }
    }

    public Task<IReadOnlyList<JiraIssueSummary>> ListSprintIssuesAsync(int sprintId, CancellationToken cancellationToken = default)
        => ListIssuesAsync($"rest/agile/1.0/sprint/{sprintId}/issue", cancellationToken);

    public async Task<IReadOnlyList<JiraIssueSummary>> ListBacklogIssuesAsync(int boardId, CancellationToken cancellationToken = default)
    {
        try
        {
            return await ListIssuesAsync($"rest/agile/1.0/board/{boardId}/backlog", cancellationToken);
        }
        catch (JiraApiException ex) when (ex.StatusCode is 400 or 404)
        {
            return [];
        }
    }

    public Task<IReadOnlyList<JiraIssueSummary>> ListBoardIssuesAsync(int boardId, CancellationToken cancellationToken = default)
        => ListIssuesAsync($"rest/agile/1.0/board/{boardId}/issue", cancellationToken);

    public async Task<JiraIssueDetails?> GetIssueAsync(string issueKey, CancellationToken cancellationToken = default)
    {
        await EnsureAuthAsync(cancellationToken);
        var encoded = Uri.EscapeDataString(issueKey.Trim());
        try
        {
            var dto = await GetJsonAsync<IssueDto>(
                $"rest/api/3/issue/{encoded}?fields=summary,description,status,issuetype,priority,assignee,labels,comment,parent",
                cancellationToken);
            if (dto?.Key is null)
                return null;

            return MapDetails(dto);
        }
        catch (JiraApiException ex) when (ex.StatusCode == 404)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<JiraIssueSummary>> ListIssuesAsync(string path, CancellationToken cancellationToken)
    {
        await EnsureAuthAsync(cancellationToken);
        const int pageSize = 50;
        const int cap = 100;
        var all = new List<JiraIssueSummary>();
        var startAt = 0;
        var separator = path.Contains('?', StringComparison.Ordinal) ? '&' : '?';

        while (startAt < cap)
        {
            var pagePath =
                $"{path}{separator}startAt={startAt}&maxResults={pageSize}&fields=summary,status,issuetype,priority,assignee";
            var page = await GetJsonAsync<IssuePageDto>(pagePath, cancellationToken);
            var issues = page?.Issues ?? [];
            if (issues.Count == 0)
                break;

            all.AddRange(issues.Select(MapSummary));
            startAt += issues.Count;
            if (page is not null && startAt >= page.Total)
                break;
            if (issues.Count < pageSize)
                break;
        }

        return all;
    }

    private JiraIssueDetails MapDetails(IssueDto dto)
    {
        var fields = dto.Fields;
        var comments = (fields?.Comment?.Comments ?? [])
            .TakeLast(8)
            .Select(c => new JiraComment(
                c.Author?.DisplayName ?? "Unknown",
                c.Created ?? string.Empty,
                c.Body.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                    ? string.Empty
                    : JiraAdfText.From(c.Body)))
            .ToList();

        var description = fields is null || fields.Description.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? string.Empty
            : JiraAdfText.From(fields.Description);

        return new JiraIssueDetails(
            dto.Key ?? string.Empty,
            dto.Id ?? string.Empty,
            fields?.Summary ?? string.Empty,
            description,
            fields?.Status?.Name ?? string.Empty,
            fields?.IssueType?.Name ?? string.Empty,
            fields?.Priority?.Name,
            fields?.Assignee?.DisplayName,
            fields?.Labels ?? [],
            fields?.Parent?.Key,
            fields?.Parent?.Fields?.Summary,
            BrowseUrl(dto.Key ?? string.Empty),
            comments);
    }

    private static JiraIssueSummary MapSummary(IssueDto dto)
    {
        var fields = dto.Fields;
        return new JiraIssueSummary(
            dto.Key ?? string.Empty,
            dto.Id ?? string.Empty,
            fields?.Summary ?? string.Empty,
            fields?.Status?.Name ?? string.Empty,
            fields?.IssueType?.Name ?? string.Empty,
            fields?.Priority?.Name,
            fields?.Assignee?.DisplayName);
    }

    private static JiraBoardInfo MapBoard(BoardDto dto) =>
        new(dto.Id, dto.Name ?? $"Board {dto.Id}", dto.Type ?? "scrum", dto.Location?.ProjectKey);

    private string BrowseUrl(string key)
    {
        var root = (http.BaseAddress?.ToString() ?? configuration["Jira:BaseUrl"] ?? string.Empty).TrimEnd('/');
        return string.IsNullOrWhiteSpace(root) ? key : $"{root}/browse/{key}";
    }

    private async Task<T?> GetJsonAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var response = await http.GetAsync(path, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new JiraApiException($"Jira resource not found ({path}).", 404);

        if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
            throw new JiraApiException("Jira rejected the credentials. Check Jira:Email and Jira:Token.", (int)response.StatusCode);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Jira GET {Path} failed: {Status} {Body}", path, response.StatusCode, Truncate(body));
            throw new JiraApiException($"Jira request failed ({(int)response.StatusCode}).", (int)response.StatusCode);
        }

        if (string.IsNullOrWhiteSpace(body))
            return default;

        return JsonSerializer.Deserialize<T>(body, JsonOptions);
    }

    private async Task EnsureAuthAsync(CancellationToken cancellationToken)
    {
        if (http.DefaultRequestHeaders.Authorization is not null)
            return;

        var token = await secrets.GetSecretAsync("Jira:Token", cancellationToken)
            ?? configuration["Jira:Token"];
        var email = await secrets.GetSecretAsync("Jira:Email", cancellationToken)
            ?? configuration["Jira:Email"];

        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Jira token is not configured. Set Jira:Token in appsettings.");

        if (!string.IsNullOrWhiteSpace(email))
        {
            var raw = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{email.Trim()}:{token.Trim()}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", raw);
        }
        else
        {
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        }

        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!http.DefaultRequestHeaders.UserAgent.Any())
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AiDevAgent/1.0");
    }

    private static string Truncate(string body) =>
        body.Length <= 300 ? body : body[..300] + "…";

    private sealed class BoardPageDto
    {
        [JsonPropertyName("values")] public List<BoardDto> Values { get; set; } = [];
    }

    private sealed class BoardDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("location")] public BoardLocationDto? Location { get; set; }
    }

    private sealed class BoardLocationDto
    {
        [JsonPropertyName("projectKey")] public string? ProjectKey { get; set; }
    }

    private sealed class SprintPageDto
    {
        [JsonPropertyName("values")] public List<SprintDto> Values { get; set; } = [];
    }

    private sealed class SprintDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("state")] public string? State { get; set; }
    }

    private sealed class IssuePageDto
    {
        [JsonPropertyName("issues")] public List<IssueDto> Issues { get; set; } = [];
        [JsonPropertyName("total")] public int Total { get; set; }
    }

    private sealed class IssueDto
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("fields")] public IssueFieldsDto? Fields { get; set; }
    }

    private sealed class IssueFieldsDto
    {
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("description")] public JsonElement Description { get; set; }
        [JsonPropertyName("status")] public NamedDto? Status { get; set; }
        [JsonPropertyName("issuetype")] public NamedDto? IssueType { get; set; }
        [JsonPropertyName("priority")] public NamedDto? Priority { get; set; }
        [JsonPropertyName("assignee")] public UserDto? Assignee { get; set; }
        [JsonPropertyName("labels")] public List<string> Labels { get; set; } = [];
        [JsonPropertyName("parent")] public ParentDto? Parent { get; set; }
        [JsonPropertyName("comment")] public CommentContainerDto? Comment { get; set; }
    }

    private sealed class NamedDto
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class UserDto
    {
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
    }

    private sealed class ParentDto
    {
        [JsonPropertyName("key")] public string? Key { get; set; }
        [JsonPropertyName("fields")] public ParentFieldsDto? Fields { get; set; }
    }

    private sealed class ParentFieldsDto
    {
        [JsonPropertyName("summary")] public string? Summary { get; set; }
    }

    private sealed class CommentContainerDto
    {
        [JsonPropertyName("comments")] public List<CommentDto> Comments { get; set; } = [];
    }

    private sealed class CommentDto
    {
        [JsonPropertyName("author")] public UserDto? Author { get; set; }
        [JsonPropertyName("created")] public string? Created { get; set; }
        [JsonPropertyName("body")] public JsonElement Body { get; set; }
    }
}
