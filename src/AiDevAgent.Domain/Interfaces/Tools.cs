namespace AiDevAgent.Domain.Interfaces;

public interface IWorkspaceManager
{
    Task<string> EnsureWorkspaceAsync(Guid workflowId, string repositoryUrl, string? branch, CancellationToken cancellationToken = default);
    string GetWorkspacePath(Guid workflowId);
    Task CleanupAsync(Guid workflowId, CancellationToken cancellationToken = default);
}

public interface IGitService
{
    Task CloneAsync(string repositoryUrl, string workspacePath, string? branch, CancellationToken cancellationToken = default);
    Task CreateBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);
    Task<string> StatusAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<string> DiffAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task CommitAsync(string workspacePath, string message, CancellationToken cancellationToken = default);
    Task PushAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);
    Task<string> GetHeadShaAsync(string workspacePath, CancellationToken cancellationToken = default);
    Task<string> CheckoutTargetBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default);
    Task<string?> GetRemoteDefaultBranchAsync(string workspacePath, CancellationToken cancellationToken = default);
}

public interface IGitLabClient
{
    Task<GitLabProjectInfo?> GetProjectAsync(string projectId, CancellationToken cancellationToken = default);
    Task<GitLabMergeRequestInfo> CreateMergeRequestAsync(
        string projectId,
        string sourceBranch,
        string targetBranch,
        string title,
        string description,
        CancellationToken cancellationToken = default);
}

public sealed record GitLabProjectInfo(
    string Id,
    string Name,
    string HttpUrlToRepo,
    string DefaultBranch);

public sealed record GitLabMergeRequestInfo(
    long Iid,
    string Url,
    string Title,
    string State);

public interface ISandboxExecutor
{
    Task<SandboxResult> RunAsync(
        string workspacePath,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

public sealed record SandboxResult(
    bool Success,
    int ExitCode,
    string StdOut,
    string StdErr,
    TimeSpan Duration);

public interface ISecretProvider
{
    Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default);
}

public interface IRepositoryTools
{
    Task<IReadOnlyList<string>> ListFilesAsync(string workspacePath, string? pattern = null, CancellationToken cancellationToken = default);
    Task<string> ReadFileAsync(string workspacePath, string relativePath, CancellationToken cancellationToken = default);
    Task WriteFileAsync(string workspacePath, string relativePath, string content, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> SearchFilesAsync(string workspacePath, string query, CancellationToken cancellationToken = default);
}
