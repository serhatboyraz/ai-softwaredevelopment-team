using System.Diagnostics;
using System.Text;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Infrastructure.Git;

public sealed class WorkspaceManager(IConfiguration configuration, ILogger<WorkspaceManager> logger) : IWorkspaceManager
{
    private string Root =>
        configuration["Workspace:Root"]
        ?? Path.Combine(Path.GetTempPath(), "aidevagent-workspaces");

    public string GetWorkspacePath(Guid workflowId)
        => Path.Combine(Root, workflowId.ToString("N"));

    public async Task<string> EnsureWorkspaceAsync(
        Guid workflowId,
        string repositoryUrl,
        string? branch,
        CancellationToken cancellationToken = default)
    {
        var path = GetWorkspacePath(workflowId);
        Directory.CreateDirectory(Root);

        if (Directory.Exists(Path.Combine(path, ".git")))
        {
            logger.LogInformation("Reusing workspace {Path} for workflow {WorkflowId}", path, workflowId);
            return path;
        }

        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);

        Directory.CreateDirectory(path);
        logger.LogInformation("Created workspace {Path} for {Repo}", path, repositoryUrl);
        await Task.CompletedTask;
        return path;
    }

    public Task CleanupAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        var path = GetWorkspacePath(workflowId);
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
                logger.LogInformation("Cleaned workspace {Path}", path);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean workspace {Path}", path);
            }
        }

        return Task.CompletedTask;
    }
}

public sealed class GitCliService(ILogger<GitCliService> logger) : IGitService
{
    public Task CloneAsync(string repositoryUrl, string workspacePath, string? branch, CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(workspacePath) && Directory.EnumerateFileSystemEntries(workspacePath).Any())
            throw new InvalidOperationException($"Workspace not empty: {workspacePath}");

        if (Directory.Exists(workspacePath))
            Directory.Delete(workspacePath);

        var parent = Path.GetDirectoryName(workspacePath)!;
        var folder = Path.GetFileName(workspacePath);
        Directory.CreateDirectory(parent);

        var cloneArgs = branch is { Length: > 0 }
            ? $"clone --branch {Escape(branch)} --single-branch {Escape(repositoryUrl)} {Escape(folder)}"
            : $"clone {Escape(repositoryUrl)} {Escape(folder)}";

        return RunGitAsync(parent, cloneArgs, cancellationToken);
    }

    public async Task CreateBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
    {
        try
        {
            await RunGitAsync(workspacePath, $"checkout -b {Escape(branchName)}", cancellationToken);
        }
        catch (InvalidOperationException)
        {
            await RunGitAsync(workspacePath, $"checkout {Escape(branchName)}", cancellationToken);
        }
    }

    public async Task<string> StatusAsync(string workspacePath, CancellationToken cancellationToken = default)
        => await RunGitCaptureAsync(workspacePath, "status --porcelain", cancellationToken);

    public async Task<string> DiffAsync(string workspacePath, CancellationToken cancellationToken = default)
        => await RunGitCaptureAsync(workspacePath, "diff", cancellationToken);

    public async Task CommitAsync(string workspacePath, string message, CancellationToken cancellationToken = default)
    {
        await RunGitAsync(workspacePath, "config user.email aidevagent@local", cancellationToken);
        await RunGitAsync(workspacePath, "config user.name AiDevAgent", cancellationToken);
        await RunGitAsync(workspacePath, "add -A", cancellationToken);
        await RunGitAsync(workspacePath, $"commit -m {Escape(message.ReplaceLineEndings(" | "))}", cancellationToken);
    }

    public Task PushAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
        => RunGitAsync(workspacePath, $"push -u origin {Escape(branchName)}", cancellationToken);

    public async Task<string> GetHeadShaAsync(string workspacePath, CancellationToken cancellationToken = default)
        => (await RunGitCaptureAsync(workspacePath, "rev-parse HEAD", cancellationToken)).Trim();

    public async Task<string> CheckoutTargetBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
    {
        await RunGitAsync(workspacePath, "fetch origin", cancellationToken);

        if (await TryCheckoutRemoteAsync(workspacePath, branchName, cancellationToken))
            return branchName;

        var fallback = await GetRemoteDefaultBranchAsync(workspacePath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(fallback)
            && !string.Equals(fallback, branchName, StringComparison.OrdinalIgnoreCase)
            && await TryCheckoutRemoteAsync(workspacePath, fallback, cancellationToken))
        {
            logger.LogWarning(
                "Branch {Requested} is not on the remote; checked out {Fallback} instead",
                branchName,
                fallback);
            return fallback;
        }

        if (await TryGitAsync(workspacePath, $"checkout -f {Escape(branchName)}", cancellationToken))
        {
            logger.LogInformation("Checked out local branch {Branch} in {Path}", branchName, workspacePath);
            return branchName;
        }

        if (!string.IsNullOrWhiteSpace(fallback)
            && await TryGitAsync(workspacePath, $"checkout -f {Escape(fallback)}", cancellationToken))
        {
            logger.LogWarning(
                "Branch {Requested} is not local either; checked out local {Fallback} instead",
                branchName,
                fallback);
            return fallback;
        }

        throw new InvalidOperationException(
            $"git checkout failed: '{branchName}' does not exist on the remote or locally"
            + (fallback is null ? "." : $", and fallback '{fallback}' could not be checked out."));
    }

    public async Task<string?> GetRemoteDefaultBranchAsync(string workspacePath, CancellationToken cancellationToken = default)
    {
        await TryGitAsync(workspacePath, "fetch origin", cancellationToken);
        await TryGitAsync(workspacePath, "remote set-head origin -a", cancellationToken);

        var (exit, stdout, _) = await ExecuteAsync(
            workspacePath, "rev-parse --abbrev-ref origin/HEAD", cancellationToken);
        if (exit == 0)
        {
            var head = stdout.Trim();
            const string prefix = "origin/";
            if (head.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                head = head[prefix.Length..];
            if (!string.IsNullOrWhiteSpace(head)
                && !head.Equals("HEAD", StringComparison.OrdinalIgnoreCase))
                return head;
        }

        foreach (var candidate in (string[]) ["main", "master", "develop"])
        {
            if (await RemoteRefExistsAsync(workspacePath, candidate, cancellationToken))
                return candidate;
        }

        return null;
    }

    private async Task<bool> TryCheckoutRemoteAsync(
        string workspacePath,
        string branchName,
        CancellationToken cancellationToken)
    {
        if (!await RemoteRefExistsAsync(workspacePath, branchName, cancellationToken)
            && !await TryGitAsync(workspacePath, $"fetch origin {Escape(branchName)}", cancellationToken))
            return false;

        if (!await TryGitAsync(
                workspacePath,
                $"checkout -f -B {Escape(branchName)} origin/{Escape(branchName)}",
                cancellationToken))
            return false;

        logger.LogInformation("Checked out {Branch} from origin in {Path}", branchName, workspacePath);
        return true;
    }

    private Task<bool> RemoteRefExistsAsync(
        string workspacePath,
        string branchName,
        CancellationToken cancellationToken)
        => TryGitAsync(
            workspacePath,
            $"show-ref --verify --quiet refs/remotes/origin/{Escape(branchName)}",
            cancellationToken);

    private async Task RunGitAsync(string workingDirectory, string args, CancellationToken cancellationToken)
    {
        var (exit, stdout, stderr) = await ExecuteAsync(workingDirectory, args, cancellationToken);
        if (exit != 0)
        {
            logger.LogError("git {Args} failed ({Exit}): {Err}", args, exit, stderr);
            throw new InvalidOperationException($"git {args} failed: {stderr}");
        }

        if (!string.IsNullOrWhiteSpace(stdout))
            logger.LogDebug("git {Args}: {Out}", args, stdout.Trim());
    }

    private async Task<bool> TryGitAsync(string workingDirectory, string args, CancellationToken cancellationToken)
    {
        var (exit, _, stderr) = await ExecuteAsync(workingDirectory, args, cancellationToken);
        if (exit == 0)
            return true;

        logger.LogDebug("git {Args} failed ({Exit}): {Err}", args, exit, stderr);
        return false;
    }

    private async Task<string> RunGitCaptureAsync(string workingDirectory, string args, CancellationToken cancellationToken)
    {
        var (exit, stdout, stderr) = await ExecuteAsync(workingDirectory, args, cancellationToken);
        if (exit != 0)
            throw new InvalidOperationException($"git {args} failed: {stderr}");
        return stdout;
    }

    private static async Task<(int Exit, string StdOut, string StdErr)> ExecuteAsync(
        string workingDirectory,
        string args,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = args,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start git process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static string Escape(string value)
    {
        if (value.All(c => !char.IsWhiteSpace(c) && c is not '"' and not '\''))
            return value;
        return $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
    }
}
