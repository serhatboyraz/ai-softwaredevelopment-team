using System.ComponentModel;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Agents.Support;

internal sealed class WorkspaceToolContext
{
    public required string WorkspacePath { get; init; }
    public List<string> ChangedFiles { get; } = [];
}

internal sealed class RepositoryAiTools(
    IRepositoryTools repository,
    WorkspaceToolContext context,
    ILogger logger)
{
    private const int MaxReadChars = 40_000;

    [Description("List files in the repository workspace. Optional glob pattern such as *.cs or package.json.")]
    public async Task<string> ListFilesAsync(
        string? pattern = null,
        CancellationToken cancellationToken = default)
    {
        var files = await repository.ListFilesAsync(context.WorkspacePath, pattern, cancellationToken);
        logger.LogInformation("Tool ListFiles pattern={Pattern} count={Count}", pattern, files.Count);
        return files.Count == 0
            ? "(no files)"
            : string.Join('\n', files.Take(400));
    }

    [Description("Read a text file from the workspace using a path relative to the repository root.")]
    public async Task<string> ReadFileAsync(
        [Description("Path relative to the repository root, for example src/WeatherApi/Program.cs.")]
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var text = await repository.ReadFileAsync(context.WorkspacePath, relativePath, cancellationToken);
            logger.LogInformation("Tool ReadFile path={Path} chars={Chars}", relativePath, text.Length);
            return text.Length <= MaxReadChars ? text : text[..MaxReadChars] + "\n…[truncated]";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool ReadFile failed path={Path}", relativePath);
            return $"ERROR reading '{relativePath}': {ex.Message}. Use a path relative to the repository root such as src/WeatherApi/Program.cs.";
        }
    }

    [Description("Search file names and text contents for a query string. Returns matching relative paths.")]
    public async Task<string> SearchFilesAsync(
        string query,
        CancellationToken cancellationToken = default)
    {
        var matches = await repository.SearchFilesAsync(context.WorkspacePath, query, cancellationToken);
        logger.LogInformation("Tool SearchFiles query={Query} matches={Count}", query, matches.Count);
        return matches.Count == 0 ? "(no matches)" : string.Join('\n', matches);
    }

    [Description("Write or overwrite a text file in the workspace.")]
    public async Task<string> WriteFileAsync(
        [Description("Path relative to the repository root, for example src/WeatherApi/Program.cs. Do not use /workspace or a host absolute path.")]
        string relativePath,
        [Description("Full file contents to write.")]
        string content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await repository.WriteFileAsync(context.WorkspacePath, relativePath, content, cancellationToken);
            var normalized = relativePath.Replace('\\', '/');
            if (!context.ChangedFiles.Contains(normalized, StringComparer.OrdinalIgnoreCase))
                context.ChangedFiles.Add(normalized);
            logger.LogInformation("Tool WriteFile path={Path}", normalized);
            return $"Wrote {normalized} ({content.Length} chars).";
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tool WriteFile failed path={Path}", relativePath);
            return $"ERROR writing '{relativePath}': {ex.Message}. Retry with a path relative to the repository root such as src/WeatherApi/Program.cs.";
        }
    }

    public IList<AITool> InspectionTools() =>
    [
        AIFunctionFactory.Create(ListFilesAsync),
        AIFunctionFactory.Create(ReadFileAsync),
        AIFunctionFactory.Create(SearchFilesAsync)
    ];

    public IList<AITool> DevelopmentTools() =>
    [
        AIFunctionFactory.Create(ListFilesAsync),
        AIFunctionFactory.Create(ReadFileAsync),
        AIFunctionFactory.Create(SearchFilesAsync),
        AIFunctionFactory.Create(WriteFileAsync)
    ];
}
