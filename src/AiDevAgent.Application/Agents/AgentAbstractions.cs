using AiDevAgent.Application.Contracts;

namespace AiDevAgent.Application.Agents;

public sealed record AgentWorkItem(
    Guid WorkflowId,
    string CorrelationId,
    Guid TaskId,
    string TaskTitle,
    string TaskDescription,
    string WorkspacePath,
    string? AnalysisJson = null,
    string? TestFailureSummary = null,
    string? UserInstructions = null);

public sealed record AgentOutcome<T>(
    T Result,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    string? Summary = null);

public sealed record GitWorkItem(
    AgentWorkItem Item,
    string GitLabProjectId,
    string RepositoryUrl,
    string DefaultBranch,
    bool TestsPassed,
    bool ContinueOnTargetBranch = false);

public sealed record GitExecutionResult(
    bool Success,
    string Branch,
    string CommitSha,
    string? MergeRequestUrl,
    long? GitLabIid,
    string Title,
    string? Description,
    string State);

public interface IPromptStore
{
    string Get(string agentName, string version = "v1");
}

public interface IContextAgent
{
    Task<AgentOutcome<ContextResult>> RunAsync(AgentWorkItem item, CancellationToken cancellationToken = default);
}

public interface IAnalysisAgent
{
    Task<AgentOutcome<AnalysisResult>> RunAsync(AgentWorkItem item, CancellationToken cancellationToken = default);
}

public interface IDevelopmentAgent
{
    Task<AgentOutcome<DevelopmentResult>> RunAsync(AgentWorkItem item, CancellationToken cancellationToken = default);
}

public interface ITestingAgent
{
    Task<AgentOutcome<TestResult>> RunAsync(AgentWorkItem item, CancellationToken cancellationToken = default);
}

public interface IGitAgent
{
    Task<AgentOutcome<GitExecutionResult>> RunAsync(GitWorkItem item, CancellationToken cancellationToken = default);
}
