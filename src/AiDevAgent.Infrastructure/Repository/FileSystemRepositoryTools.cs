using AiDevAgent.Application.Workspace;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Infrastructure.Repository;

public sealed class FileSystemRepositoryTools(ILogger<FileSystemRepositoryTools> logger) : IRepositoryTools
{
    public Task<IReadOnlyList<string>> ListFilesAsync(string workspacePath, string? pattern = null, CancellationToken cancellationToken = default)
    {
        EnsureInside(workspacePath, workspacePath);
        var files = Directory.EnumerateFiles(workspacePath, pattern ?? "*", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(f => Path.GetRelativePath(workspacePath, f).Replace('\\', '/'))
            .OrderBy(f => f)
            .Take(5000)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public async Task<string> ReadFileAsync(string workspacePath, string relativePath, CancellationToken cancellationToken = default)
    {
        var full = Resolve(workspacePath, relativePath);
        return await File.ReadAllTextAsync(full, cancellationToken);
    }

    public async Task WriteFileAsync(string workspacePath, string relativePath, string content, CancellationToken cancellationToken = default)
    {
        var full = Resolve(workspacePath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        await File.WriteAllTextAsync(full, content, cancellationToken);
        logger.LogInformation("Wrote {Path}", relativePath);
    }

    public async Task<IReadOnlyList<string>> SearchFilesAsync(string workspacePath, string query, CancellationToken cancellationToken = default)
    {
        var matches = new List<string>();
        foreach (var relative in await ListFilesAsync(workspacePath, cancellationToken: cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (relative.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(relative);
                continue;
            }

            try
            {
                var text = await ReadFileAsync(workspacePath, relative, cancellationToken);
                if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                    matches.Add(relative);
            }
            catch
            {
                // skip binary / unreadable
            }

            if (matches.Count >= 200) break;
        }

        return matches;
    }

    private static string Resolve(string workspacePath, string relativePath)
        => WorkspacePaths.ResolveInside(workspacePath, relativePath);

    private static void EnsureInside(string workspacePath, string fullPath)
    {
        if (!WorkspacePaths.IsInside(workspacePath, fullPath))
            throw new InvalidOperationException("Path escapes workspace.");
    }
}
