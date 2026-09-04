using AiDevAgent.Application.DTOs;
using AiDevAgent.Domain.Entities;
using AiDevAgent.Domain.Enums;

namespace AiDevAgent.Application.Workflows;

public static class WorkflowProgressMapper
{
    public static IReadOnlyList<AgentRunDto> ToAgentRunDtos(Workflow workflow)
        => workflow.AgentRuns
            .OrderBy(r => r.StartedAt ?? r.CreatedAt)
            .Select(r => new AgentRunDto(
                r.Id,
                r.AgentType,
                r.OutputSummary,
                r.Success,
                r.StartedAt,
                r.CompletedAt,
                r.PromptTokens,
                r.CompletionTokens))
            .ToList();

    public static IReadOnlyList<WorkflowStepProgressDto> Build(Workflow workflow, DateTimeOffset? now = null)
    {
        var clock = now ?? DateTimeOffset.UtcNow;
        var runs = workflow.AgentRuns.OrderBy(r => r.StartedAt ?? r.CreatedAt).ToList();
        var developmentRuns = runs.Where(r => r.AgentType == AgentType.Development).ToList();
        var initialDev = developmentRuns.Take(1).ToList();
        var fixDev = developmentRuns.Skip(1).ToList();

        return
        [
            MapStep(
                workflow, clock, runs,
                key: "context",
                label: "Context",
                runsForStep: runs.Where(r => r.AgentType == AgentType.Context),
                activeWhen: s => s is WorkflowState.Pending or WorkflowState.ContextCheck,
                waitingWhen: s => s is WorkflowState.WaitingForInformation,
                completedWhen: HasLeftContext),
            MapStep(
                workflow, clock, runs,
                key: "analysis",
                label: "Analysis",
                runsForStep: runs.Where(r => r.AgentType == AgentType.Analysis),
                activeWhen: s => s is WorkflowState.Analyzing,
                waitingWhen: _ => false,
                completedWhen: HasLeftAnalysis),
            MapStep(
                workflow, clock, runs,
                key: "development",
                label: "Development",
                runsForStep: initialDev,
                activeWhen: s => s is WorkflowState.Developing,
                waitingWhen: _ => false,
                completedWhen: s => HasLeftDevelopment(s) || s is WorkflowState.Fixing),
            MapStep(
                workflow, clock, runs,
                key: "testing",
                label: "Testing",
                runsForStep: runs.Where(r => r.AgentType == AgentType.Testing),
                activeWhen: s => s is WorkflowState.Testing,
                waitingWhen: _ => false,
                completedWhen: HasLeftTesting),
            MapStep(
                workflow, clock, runs,
                key: "fixing",
                label: "Fixing",
                runsForStep: fixDev,
                activeWhen: s => s is WorkflowState.Fixing,
                waitingWhen: _ => false,
                completedWhen: s => workflow.RepairAttempt > 0 && HasLeftTesting(s),
                idleWhenNeverStarted: workflow.RepairAttempt == 0 && workflow.State is not WorkflowState.Fixing),
            MapStep(
                workflow, clock, runs,
                key: "git",
                label: "Git / MR",
                runsForStep: runs.Where(r => r.AgentType == AgentType.Git),
                activeWhen: s => s is WorkflowState.ReadyForMr,
                waitingWhen: s => s is WorkflowState.WaitingForApproval,
                completedWhen: s => s is WorkflowState.MrCreated or WorkflowState.Completed || workflow.MergeRequest is not null),
            MapStep(
                workflow, clock, runs,
                key: "completed",
                label: "Completed",
                runsForStep: [],
                activeWhen: _ => false,
                waitingWhen: _ => false,
                completedWhen: s => s is WorkflowState.Completed)
        ];
    }

    private static WorkflowStepProgressDto MapStep(
        Workflow workflow,
        DateTimeOffset now,
        IReadOnlyList<AgentRun> allRuns,
        string key,
        string label,
        IEnumerable<AgentRun> runsForStep,
        Func<WorkflowState, bool> activeWhen,
        Func<WorkflowState, bool> waitingWhen,
        Func<WorkflowState, bool> completedWhen,
        bool idleWhenNeverStarted = false)
    {
        var stepRuns = runsForStep.ToList();
        var state = workflow.State;
        var terminalFailed = state is WorkflowState.Failed;
        var terminalCancelled = state is WorkflowState.Cancelled;
        var terminal = terminalFailed || terminalCancelled;

        DateTimeOffset? startedAt = stepRuns.Count == 0
            ? null
            : stepRuns.Min(r => r.StartedAt ?? r.CreatedAt);
        DateTimeOffset? completedAt = stepRuns.Count > 0 && stepRuns.All(r => r.CompletedAt is not null)
            ? stepRuns.Max(r => r.CompletedAt)
            : null;

        var isWaiting = waitingWhen(state);
        var isActive = !terminal && (activeWhen(state) || isWaiting);
        var reachedDone = completedWhen(state)
            || (state is WorkflowState.Completed && key is not "fixing")
            || (key is "fixing" && workflow.RepairAttempt > 0 && HasLeftTesting(state));
        var isCompleted = !isActive && reachedDone && !terminalFailed;

        string status;
        if (isActive)
            status = isWaiting ? "waiting" : "running";
        else if (terminalFailed && WasCurrentWhenFailed(key, workflow, allRuns))
            status = "failed";
        else if (isCompleted)
            status = "completed";
        else if (idleWhenNeverStarted && stepRuns.Count == 0)
            status = terminal || state is WorkflowState.Completed ? "skipped" : "pending";
        else if (terminal && !isCompleted)
            status = "skipped";
        else
            status = "pending";

        if (isActive && startedAt is null)
            startedAt = InferRunningStart(workflow, key);

        if (isCompleted && completedAt is null)
            completedAt = workflow.CompletedAt ?? workflow.UpdatedAt;

        var durationMs = MeasureDuration(stepRuns, startedAt, completedAt, isActive, now);
        var prompt = stepRuns.Sum(r => r.PromptTokens ?? 0);
        var completion = stepRuns.Sum(r => r.CompletionTokens ?? 0);
        var summary = stepRuns.LastOrDefault(r => !string.IsNullOrWhiteSpace(r.OutputSummary))?.OutputSummary
            ?? (isWaiting ? WaitingSummary(state) : null);

        return new WorkflowStepProgressDto(
            key,
            label,
            status,
            startedAt,
            isActive ? null : completedAt,
            durationMs,
            prompt,
            completion,
            prompt + completion,
            summary);
    }

    private static DateTimeOffset? InferRunningStart(Workflow workflow, string key)
    {
        var events = workflow.Events.OrderBy(e => e.OccurredAt).ToList();
        WorkflowEvent? match = key switch
        {
            "context" => events.LastOrDefault(e => e.EventType is "workflow.started" or "workflow.created"),
            "analysis" => events.LastOrDefault(e => e.EventType == "agent.completed"),
            "development" => events.LastOrDefault(e => e.EventType is "agent.completed" or "workflow.rerun"),
            "testing" => events.LastOrDefault(e => e.EventType == "agent.completed"),
            "fixing" => events.LastOrDefault(e => e.EventType == "test.completed"),
            "git" => events.LastOrDefault(e => e.EventType is "test.completed" or "human.approved" or "human.approval_required"),
            _ => null
        };
        return match?.OccurredAt ?? workflow.UpdatedAt;
    }

    private static string? WaitingSummary(WorkflowState state) => state switch
    {
        WorkflowState.WaitingForInformation => "Waiting for more information.",
        WorkflowState.WaitingForApproval => "Waiting for approval before opening a merge request.",
        _ => null
    };

    private static long MeasureDuration(
        IReadOnlyList<AgentRun> runs,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        bool running,
        DateTimeOffset now)
    {
        long fromRuns = 0;
        foreach (var run in runs)
        {
            var start = run.StartedAt ?? run.CreatedAt;
            var end = run.CompletedAt ?? (running ? now : start);
            if (end > start)
                fromRuns += (long)(end - start).TotalMilliseconds;
        }

        if (fromRuns > 0)
            return fromRuns;
        if (startedAt is null)
            return 0;
        var endWall = completedAt ?? (running ? now : startedAt.Value);
        return Math.Max(0, (long)(endWall - startedAt.Value).TotalMilliseconds);
    }

    private static bool HasLeftContext(WorkflowState s) => s is
        WorkflowState.Analyzing or WorkflowState.Developing or WorkflowState.Testing or
        WorkflowState.Fixing or WorkflowState.ReadyForMr or WorkflowState.MrCreated or
        WorkflowState.WaitingForApproval or WorkflowState.Completed;

    private static bool HasLeftAnalysis(WorkflowState s) => s is
        WorkflowState.Developing or WorkflowState.Testing or WorkflowState.Fixing or
        WorkflowState.ReadyForMr or WorkflowState.MrCreated or
        WorkflowState.WaitingForApproval or WorkflowState.Completed;

    private static bool HasLeftDevelopment(WorkflowState s) => s is
        WorkflowState.Testing or WorkflowState.ReadyForMr or WorkflowState.MrCreated or
        WorkflowState.WaitingForApproval or WorkflowState.Completed;

    private static bool HasLeftTesting(WorkflowState s) => s is
        WorkflowState.ReadyForMr or WorkflowState.MrCreated or
        WorkflowState.WaitingForApproval or WorkflowState.Completed;

    private static bool WasCurrentWhenFailed(string key, Workflow workflow, IReadOnlyList<AgentRun> runs)
    {
        if (workflow.State is not WorkflowState.Failed)
            return false;

        if (!string.IsNullOrWhiteSpace(workflow.ErrorMessage))
        {
            var msg = workflow.ErrorMessage;
            if (msg.Contains("Tests failed", StringComparison.OrdinalIgnoreCase))
                return key == "testing";
            if (msg.Contains("Development", StringComparison.OrdinalIgnoreCase))
                return key == "development";
        }

        var last = runs.LastOrDefault();
        return last?.AgentType switch
        {
            AgentType.Context => key == "context",
            AgentType.Analysis => key == "analysis",
            AgentType.Development => key is "development" or "fixing",
            AgentType.Testing => key is "testing" or "fixing",
            AgentType.Git => key == "git",
            _ => key == "context"
        };
    }
}
