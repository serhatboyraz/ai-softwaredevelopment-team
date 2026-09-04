using System.Text.Json;
using AiDevAgent.Application.Agents;
using AiDevAgent.Application.Contracts;
using AiDevAgent.Domain.Entities;
using AiDevAgent.Domain.Enums;
using AiDevAgent.Domain.Interfaces;
using AiDevAgent.Application.Git;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using DomainTaskStatus = AiDevAgent.Domain.Enums.TaskStatus;

namespace AiDevAgent.Application.Workflows;

/// <summary>
/// Deterministic orchestrator. Agents reason; this class owns state, retries, approval, and GitLab side effects.
/// Resumes from the current workflow state so approval / waiting-for-information can continue later.
/// </summary>
public sealed class AgentWorkflowExecutor(
    IWorkflowRepository workflows,
    IDevTaskRepository tasks,
    IProjectRepository projects,
    IUnitOfWork unitOfWork,
    IWorkflowEventPublisher events,
    IWorkspaceManager workspaces,
    IGitService git,
    ISecretProvider secrets,
    IContextAgent contextAgent,
    IAnalysisAgent analysisAgent,
    IDevelopmentAgent developmentAgent,
    ITestingAgent testingAgent,
    IGitAgent gitAgent,
    ITaskStepArtifactWriter artifacts,
    IConfiguration configuration,
    ILogger<AgentWorkflowExecutor> logger) : IWorkflowExecutor
{
    public async Task ExecuteAsync(Guid workflowId, CancellationToken cancellationToken = default)
    {
        var workflow = await workflows.GetByIdWithDetailsAsync(workflowId, cancellationToken);
        if (workflow is null)
        {
            logger.LogWarning("Workflow {WorkflowId} not found", workflowId);
            return;
        }

        if (workflow.State is WorkflowState.Completed or WorkflowState.Cancelled or WorkflowState.Failed)
            return;
        if (workflow.State is WorkflowState.WaitingForInformation or WorkflowState.WaitingForApproval)
            return;

        var task = await tasks.GetByIdAsync(workflow.TaskId, cancellationToken);
        if (task is null) return;
        var project = await projects.GetByIdAsync(task.ProjectId, cancellationToken);

        using var scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["WorkflowId"] = workflow.Id,
            ["TaskId"] = task.Id,
            ["CorrelationId"] = workflow.CorrelationId
        });

        try
        {
            workflow.StartedAt ??= DateTimeOffset.UtcNow;
            task.Status = DomainTaskStatus.InProgress;
            task.UpdatedAt = DateTimeOffset.UtcNow;

            var workspacePath = await workspaces.EnsureWorkspaceAsync(
                workflow.Id,
                project?.RepositoryUrl ?? string.Empty,
                project?.DefaultBranch,
                cancellationToken);
            workflow.WorkspacePath = workspacePath;

            await MaybeCloneAsync(workflow, project, workspacePath, cancellationToken);

            var userCommands = await workflows.ListUserCommandsAsync(workflow.Id, cancellationToken);
            var followUp = userCommands.Count > 0 && (
                workflow.MergeRequest is not null
                || !string.IsNullOrWhiteSpace(workflow.CommitSha)
                || !string.IsNullOrWhiteSpace(workflow.BranchName));
            var defaultBranch = await ResolveDefaultBranchAsync(project, workspacePath, cancellationToken);
            var followUpBranch = !string.IsNullOrWhiteSpace(workflow.BranchName)
                ? workflow.BranchName
                : defaultBranch;

            if (followUp)
                await MaybeCheckoutFollowUpBranchAsync(workflow, workspacePath, followUpBranch, cancellationToken);

            async Task<AgentWorkItem> ItemAsync()
            {
                var commands = await workflows.ListUserCommandsAsync(workflow.Id, cancellationToken);
                var extra = commands.Count == 0
                    ? null
                    : string.Join('\n', commands.Select(c => "- " + c));
                return new AgentWorkItem(
                    workflow.Id,
                    workflow.CorrelationId,
                    task.Id,
                    task.Title,
                    task.Description,
                    workspacePath,
                    workflow.AnalysisResultJson,
                    UserInstructions: extra);
            }

            if (workflow.State is WorkflowState.Pending or WorkflowState.ContextCheck)
            {
                await TransitionAsync(workflow, WorkflowState.ContextCheck, "workflow.started",
                    $"Context check started. Workspace: {workspacePath}", cancellationToken);

                var contextStarted = DateTimeOffset.UtcNow;
                var context = await contextAgent.RunAsync(await ItemAsync(), cancellationToken);
                await RecordAgentRunAsync(workflow, task.Title, AgentType.Context, context.Summary, context.Result, context.PromptTokens, context.CompletionTokens, contextStarted, cancellationToken);
                workflow.ContextResultJson = JsonSerializer.Serialize(context.Result);

                if (!context.Result.Ready)
                {
                    task.Status = DomainTaskStatus.WaitingForUser;
                    task.UpdatedAt = DateTimeOffset.UtcNow;
                    var questions = context.Result.Questions.Count == 0
                        ? "More information is required."
                        : string.Join(" ", context.Result.Questions);
                    await TransitionAsync(workflow, WorkflowState.WaitingForInformation, "agent.completed",
                        context.Summary ?? questions, cancellationToken);
                    return;
                }

                await TransitionAsync(workflow, WorkflowState.Analyzing, "agent.completed",
                    context.Summary ?? "Repository context is sufficient.", cancellationToken);
            }

            if (await StopIfCancelledAsync(workflow, task, cancellationToken)) return;

            if (workflow.State is WorkflowState.Analyzing)
            {
                var analysisStarted = DateTimeOffset.UtcNow;
                var analysis = await analysisAgent.RunAsync(await ItemAsync(), cancellationToken);
                await RecordAgentRunAsync(workflow, task.Title, AgentType.Analysis, analysis.Summary, analysis.Result, analysis.PromptTokens, analysis.CompletionTokens, analysisStarted, cancellationToken);
                workflow.AnalysisResultJson = JsonSerializer.Serialize(analysis.Result);
                await TransitionAsync(workflow, WorkflowState.Developing, "agent.completed",
                    analysis.Summary ?? "Implementation plan prepared.", cancellationToken);
            }

            TestResult? lastTest = null;

            while (workflow.State is WorkflowState.Developing or WorkflowState.Fixing or WorkflowState.Testing)
            {
                if (await StopIfCancelledAsync(workflow, task, cancellationToken)) return;

                if (workflow.State is WorkflowState.Developing or WorkflowState.Fixing)
                {
                    var work = await ItemAsync();
                    work = work with { TestFailureSummary = lastTest is { Success: false } ? string.Join('\n', lastTest.Failures) : null };
                    var developmentStarted = DateTimeOffset.UtcNow;
                    var development = await developmentAgent.RunAsync(work, cancellationToken);
                    await RecordAgentRunAsync(workflow, task.Title, AgentType.Development, development.Summary, development.Result, development.PromptTokens, development.CompletionTokens, developmentStarted, cancellationToken);
                    if (!development.Result.Success)
                    {
                        await FailAsync(workflow, task, development.Summary ?? "Development Agent failed.", cancellationToken);
                        return;
                    }

                    await TransitionAsync(workflow, WorkflowState.Testing, "agent.completed",
                        development.Summary ?? "Code changes applied.", cancellationToken);
                }

                if (workflow.State is WorkflowState.Testing)
                {
                    var testingStarted = DateTimeOffset.UtcNow;
                    var testing = await testingAgent.RunAsync(await ItemAsync(), cancellationToken);
                    lastTest = testing.Result;
                    await RecordAgentRunAsync(workflow, task.Title, AgentType.Testing, testing.Summary, testing.Result, testing.PromptTokens, testing.CompletionTokens, testingStarted, cancellationToken);
                    await workflows.AddTestRunAsync(new TestRun
                    {
                        WorkflowId = workflow.Id,
                        Success = testing.Result.Success,
                        BuildPassed = testing.Result.BuildPassed,
                        Passed = testing.Result.Passed,
                        Failed = testing.Result.Failed,
                        FailuresJson = JsonSerializer.Serialize(testing.Result.Failures),
                        LogSummary = testing.Summary,
                        StartedAt = testingStarted,
                        CompletedAt = DateTimeOffset.UtcNow
                    }, cancellationToken);

                    if (testing.Result.Success)
                    {
                        await TransitionAsync(workflow, WorkflowState.ReadyForMr, "test.completed",
                            testing.Summary ?? "Build and tests passed.", cancellationToken);
                        break;
                    }

                    if (testing.Result.InfrastructureFailure)
                    {
                        await FailAsync(
                            workflow,
                            task,
                            testing.Summary ?? "Sandbox infrastructure failure; source repair cannot fix this.",
                            cancellationToken);
                        return;
                    }

                    if (workflow.RepairAttempt >= workflow.MaxRepairAttempts)
                    {
                        await FailAsync(workflow, task, $"Tests failed after {workflow.MaxRepairAttempts} repair attempt(s).", cancellationToken);
                        return;
                    }

                    workflow.RepairAttempt++;
                    await TransitionAsync(workflow, WorkflowState.Fixing, "test.completed",
                        $"{testing.Summary} Starting repair {workflow.RepairAttempt}/{workflow.MaxRepairAttempts}.", cancellationToken);
                }
            }

            if (await StopIfCancelledAsync(workflow, task, cancellationToken)) return;

            if (workflow.State is WorkflowState.ReadyForMr
                && configuration.GetValue("Workflow:RequireApprovalBeforeMr", false)
                && workflow.ApprovalRequests.All(a => a.Status != ApprovalStatus.Approved))
            {
                await workflows.AddApprovalRequestAsync(new ApprovalRequest
                {
                    WorkflowId = workflow.Id,
                    Reason = "Approval required before creating a merge request.",
                    Status = ApprovalStatus.Pending
                }, cancellationToken);
                await TransitionAsync(workflow, WorkflowState.WaitingForApproval, "human.approval_required",
                    "Waiting for human approval before opening a merge request.", cancellationToken);
                return;
            }

            if (workflow.State is WorkflowState.ReadyForMr)
            {
                var gitStarted = DateTimeOffset.UtcNow;
                var gitOutcome = await gitAgent.RunAsync(new GitWorkItem(
                    await ItemAsync(),
                    project?.GitLabProjectId ?? string.Empty,
                    project?.RepositoryUrl ?? string.Empty,
                    followUp ? (workflow.BranchName ?? defaultBranch) : defaultBranch,
                    lastTest?.Success ?? true,
                    ContinueOnTargetBranch: followUp), cancellationToken);

                await RecordAgentRunAsync(workflow, task.Title, AgentType.Git, gitOutcome.Summary, gitOutcome.Result, gitOutcome.PromptTokens, gitOutcome.CompletionTokens, gitStarted, cancellationToken);
                await CommitPendingArtifactsAsync(workspacePath, gitOutcome.Result.Branch, task.Title, cancellationToken);
                workflow.BranchName = gitOutcome.Result.Branch;
                if (Directory.Exists(Path.Combine(workspacePath, ".git")))
                    workflow.CommitSha = await git.GetHeadShaAsync(workspacePath, cancellationToken);
                else
                    workflow.CommitSha = gitOutcome.Result.CommitSha;

                if (workflow.MergeRequest is null)
                {
                    await workflows.AddMergeRequestAsync(new MergeRequest
                    {
                        WorkflowId = workflow.Id,
                        GitLabIid = gitOutcome.Result.GitLabIid,
                        Url = gitOutcome.Result.MergeRequestUrl,
                        Title = gitOutcome.Result.Title,
                        Description = gitOutcome.Result.Description,
                        State = gitOutcome.Result.State
                    }, cancellationToken);
                }
                else if (followUp)
                {
                    workflow.MergeRequest.Url = gitOutcome.Result.MergeRequestUrl;
                    workflow.MergeRequest.State = gitOutcome.Result.State;
                    workflow.MergeRequest.Title = gitOutcome.Result.Title;
                    workflow.MergeRequest.Description = gitOutcome.Result.Description;
                    workflow.MergeRequest.UpdatedAt = DateTimeOffset.UtcNow;
                }

                var gitEvent = followUp ? "git.pushed" : "mr.created";
                await TransitionAsync(workflow, WorkflowState.MrCreated, gitEvent,
                    gitOutcome.Summary ?? $"Merge request on {gitOutcome.Result.Branch}.", cancellationToken);

                task.Status = DomainTaskStatus.Completed;
                task.UpdatedAt = DateTimeOffset.UtcNow;
                await TransitionAsync(workflow, WorkflowState.Completed, "workflow.completed",
                    "Workflow completed.", cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("Workflow {WorkflowId} cancelled or host stopping", workflowId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Workflow {WorkflowId} failed", workflowId);
            try
            {
                await FailAsync(workflow, task, ex.Message, cancellationToken);
            }
            catch (Exception saveEx)
            {
                logger.LogError(saveEx, "Failed to persist failure state for workflow {WorkflowId}", workflowId);
            }
        }
    }

    private async Task MaybeCloneAsync(Workflow workflow, Project? project, string workspacePath, CancellationToken cancellationToken)
    {
        if (project is null || string.IsNullOrWhiteSpace(project.RepositoryUrl))
            return;
        if (Directory.Exists(Path.Combine(workspacePath, ".git")))
            return;

        var token = await secrets.GetSecretAsync("GitLab:Token", cancellationToken)
            ?? configuration["GitLab:Token"];
        var enableClone = configuration.GetValue("Workflow:EnableGitClone", false)
            || !string.IsNullOrWhiteSpace(token);
        if (!enableClone)
            return;

        var cloneUrl = GitCloneUrl.WithOptionalToken(project.RepositoryUrl, token);
        var requested = string.IsNullOrWhiteSpace(project.DefaultBranch) ? null : project.DefaultBranch;
        try
        {
            await git.CloneAsync(cloneUrl, workspacePath, requested, cancellationToken);
        }
        catch (InvalidOperationException ex) when (requested is not null)
        {
            logger.LogWarning(ex, "Clone of branch {Branch} failed; cloning the remote default instead", requested);
            ResetWorkspace(workspacePath);
            await git.CloneAsync(cloneUrl, workspacePath, branch: null, cancellationToken);
            var detected = await git.GetRemoteDefaultBranchAsync(workspacePath, cancellationToken);
            if (!string.IsNullOrWhiteSpace(detected))
                project.DefaultBranch = detected;
        }

        await AppendEventAsync(workflow, "git.cloned", $"Cloned {GitCloneUrl.Redact(project.RepositoryUrl)}", cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task MaybeCheckoutFollowUpBranchAsync(
        Workflow workflow,
        string workspacePath,
        string targetBranch,
        CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path.Combine(workspacePath, ".git")))
            return;

        var status = await git.StatusAsync(workspacePath, cancellationToken);
        if (!string.IsNullOrWhiteSpace(status))
        {
            logger.LogInformation(
                "Skipping follow-up checkout of {Branch}; workspace has uncommitted changes",
                targetBranch);
            return;
        }

        var actual = await git.CheckoutTargetBranchAsync(workspacePath, targetBranch, cancellationToken);
        workflow.BranchName = actual;
        await AppendEventAsync(
            workflow,
            "git.checkout",
            actual == targetBranch
                ? $"Switched to {actual} for the next commit."
                : $"Branch {targetBranch} was missing; switched to {actual} for the next commit.",
            cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> ResolveDefaultBranchAsync(
        Project? project,
        string workspacePath,
        CancellationToken cancellationToken)
    {
        var configured = GitBranches.OrFallback(project?.DefaultBranch);
        if (!Directory.Exists(Path.Combine(workspacePath, ".git")))
            return configured;

        var detected = await git.GetRemoteDefaultBranchAsync(workspacePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(detected))
            return configured;

        if (project is not null && string.IsNullOrWhiteSpace(project.DefaultBranch))
        {
            logger.LogInformation("Detected remote default branch {Detected}", detected);
            project.DefaultBranch = detected;
            await unitOfWork.SaveChangesAsync(cancellationToken);
            return detected;
        }

        return configured;
    }

    private static void ResetWorkspace(string workspacePath)
    {
        if (Directory.Exists(workspacePath))
            Directory.Delete(workspacePath, recursive: true);
    }

    private async Task<bool> StopIfCancelledAsync(Workflow workflow, DevTask task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await workflows.IsCancelledAsync(workflow.Id, cancellationToken))
            return false;

        workflow.State = WorkflowState.Cancelled;
        workflow.CompletedAt ??= DateTimeOffset.UtcNow;
        workflow.UpdatedAt = DateTimeOffset.UtcNow;
        task.Status = DomainTaskStatus.Cancelled;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task FailAsync(Workflow workflow, DevTask task, string message, CancellationToken cancellationToken)
    {
        workflow.State = WorkflowState.Failed;
        workflow.ErrorMessage = message;
        workflow.CompletedAt = DateTimeOffset.UtcNow;
        workflow.UpdatedAt = DateTimeOffset.UtcNow;
        task.Status = DomainTaskStatus.Failed;
        task.UpdatedAt = DateTimeOffset.UtcNow;
        await AppendEventAsync(workflow, "workflow.failed", message, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(workflow.Id, "workflow.failed", message, cancellationToken: cancellationToken);
    }

    private async Task RecordAgentRunAsync<T>(
        Workflow workflow,
        string taskTitle,
        AgentType type,
        string? summary,
        T result,
        int? promptTokens,
        int? completionTokens,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        await workflows.AddAgentRunAsync(new AgentRun
        {
            WorkflowId = workflow.Id,
            AgentType = type,
            InputSummary = type.ToString(),
            OutputSummary = summary,
            OutputJson = JsonSerializer.Serialize(result),
            Success = true,
            StartedAt = startedAt,
            CompletedAt = DateTimeOffset.UtcNow,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens
        }, cancellationToken);

        await artifacts.WriteAsync(
            workflow.WorkspacePath ?? string.Empty,
            taskTitle,
            type,
            summary,
            result!,
            cancellationToken);
    }

    /// <summary>
    /// Commits and pushes any ai-tasks markdown written after the main Git agent commit (e.g. git.md).
    /// </summary>
    private async Task CommitPendingArtifactsAsync(
        string workspacePath,
        string branch,
        string taskTitle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workspacePath)
            || string.IsNullOrWhiteSpace(branch)
            || !Directory.Exists(Path.Combine(workspacePath, ".git")))
            return;

        var status = await git.StatusAsync(workspacePath, cancellationToken);
        if (string.IsNullOrWhiteSpace(status))
            return;

        var message = $"docs(ai-tasks): record steps for {taskTitle}";
        await git.CommitAsync(workspacePath, message, cancellationToken);
        try
        {
            await git.PushAsync(workspacePath, branch, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to push ai-tasks artifact commit on {Branch}", branch);
        }
    }

    private async Task TransitionAsync(
        Workflow workflow,
        WorkflowState state,
        string eventType,
        string message,
        CancellationToken cancellationToken)
    {
        workflow.State = state;
        workflow.UpdatedAt = DateTimeOffset.UtcNow;
        if (state is WorkflowState.Completed or WorkflowState.Failed or WorkflowState.Cancelled)
            workflow.CompletedAt ??= DateTimeOffset.UtcNow;

        await AppendEventAsync(workflow, eventType, message, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await events.PublishAsync(workflow.Id, eventType, message, cancellationToken: cancellationToken);
        logger.LogInformation("Workflow {WorkflowId} → {State}: {Message}", workflow.Id, state, message);
    }

    private async Task AppendEventAsync(Workflow workflow, string eventType, string message, CancellationToken cancellationToken)
    {
        await workflows.AddEventAsync(new WorkflowEvent
        {
            WorkflowId = workflow.Id,
            EventType = eventType,
            Message = message
        }, cancellationToken);
    }
}
