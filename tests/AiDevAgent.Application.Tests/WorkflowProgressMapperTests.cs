using AiDevAgent.Application.Workflows;
using AiDevAgent.Domain.Entities;
using AiDevAgent.Domain.Enums;

namespace AiDevAgent.Application.Tests;

public class WorkflowProgressMapperTests
{
    [Fact]
    public void Pending_MarksContextRunning_AndLaterStepsPending()
    {
        var workflow = new Workflow { State = WorkflowState.Pending };
        var now = DateTimeOffset.Parse("2026-09-04T12:00:00Z");

        var steps = WorkflowProgressMapper.Build(workflow, now).ToDictionary(s => s.Key);

        Assert.Equal("running", steps["context"].Status);
        Assert.Equal("pending", steps["analysis"].Status);
        Assert.Equal("pending", steps["fixing"].Status);
        Assert.Equal("pending", steps["completed"].Status);
    }

    [Fact]
    public void Developing_HighlightsDevelopment_AndSumsTokensAndDuration()
    {
        var started = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var now = started.AddMinutes(2);
        var workflow = new Workflow { State = WorkflowState.Developing };
        workflow.AgentRuns.Add(Run(AgentType.Context, started, started.AddSeconds(20), 100, 40));
        workflow.AgentRuns.Add(Run(AgentType.Analysis, started.AddSeconds(20), started.AddSeconds(50), 200, 80));

        var steps = WorkflowProgressMapper.Build(workflow, now).ToDictionary(s => s.Key);

        Assert.Equal("completed", steps["context"].Status);
        Assert.Equal("completed", steps["analysis"].Status);
        Assert.Equal("running", steps["development"].Status);
        Assert.Equal(140, steps["context"].TotalTokens);
        Assert.Equal(20_000, steps["context"].DurationMs);
        Assert.Equal(280, steps["analysis"].TotalTokens);
        Assert.Equal("pending", steps["testing"].Status);
    }

    [Fact]
    public void Completed_MarksPipelineDone_AndLeavesUnusedFixingSkipped()
    {
        var started = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var workflow = new Workflow
        {
            State = WorkflowState.Completed,
            CompletedAt = started.AddMinutes(5),
            MergeRequest = new MergeRequest { Title = "MR" }
        };
        workflow.AgentRuns.Add(Run(AgentType.Context, started, started.AddSeconds(5), 10, 5));
        workflow.AgentRuns.Add(Run(AgentType.Analysis, started.AddSeconds(5), started.AddSeconds(15), 20, 10));
        workflow.AgentRuns.Add(Run(AgentType.Development, started.AddSeconds(15), started.AddSeconds(40), 300, 120));
        workflow.AgentRuns.Add(Run(AgentType.Testing, started.AddSeconds(40), started.AddSeconds(55), 40, 10));
        workflow.AgentRuns.Add(Run(AgentType.Git, started.AddSeconds(55), started.AddSeconds(70), 15, 8));

        var steps = WorkflowProgressMapper.Build(workflow, started.AddMinutes(5)).ToDictionary(s => s.Key);

        Assert.Equal("completed", steps["context"].Status);
        Assert.Equal("completed", steps["analysis"].Status);
        Assert.Equal("completed", steps["development"].Status);
        Assert.Equal("completed", steps["testing"].Status);
        Assert.Equal("skipped", steps["fixing"].Status);
        Assert.Equal("completed", steps["git"].Status);
        Assert.Equal("completed", steps["completed"].Status);
        Assert.Equal(23, steps["git"].TotalTokens);
    }

    [Fact]
    public void FixingLoop_UsesLaterDevelopmentRuns()
    {
        var started = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var workflow = new Workflow { State = WorkflowState.Fixing, RepairAttempt = 1 };
        workflow.AgentRuns.Add(Run(AgentType.Development, started, started.AddSeconds(10), 100, 50));
        workflow.AgentRuns.Add(Run(AgentType.Testing, started.AddSeconds(10), started.AddSeconds(20), 30, 10));

        var steps = WorkflowProgressMapper.Build(workflow, started.AddSeconds(30)).ToDictionary(s => s.Key);

        Assert.Equal("completed", steps["development"].Status);
        Assert.Equal("running", steps["fixing"].Status);
        Assert.Equal(150, steps["development"].TotalTokens);
        Assert.Equal(0, steps["fixing"].TotalTokens);
    }

    [Fact]
    public void FailedTests_MarksTestingFailed()
    {
        var workflow = new Workflow
        {
            State = WorkflowState.Failed,
            ErrorMessage = "Tests failed after 3 repair attempt(s)."
        };
        workflow.AgentRuns.Add(Run(AgentType.Testing, DateTimeOffset.UtcNow.AddSeconds(-10), DateTimeOffset.UtcNow, 10, 2));

        var steps = WorkflowProgressMapper.Build(workflow).ToDictionary(s => s.Key);

        Assert.Equal("failed", steps["testing"].Status);
    }

    private static AgentRun Run(
        AgentType type,
        DateTimeOffset started,
        DateTimeOffset completed,
        int prompt,
        int completion)
        => new()
        {
            AgentType = type,
            Success = true,
            StartedAt = started,
            CompletedAt = completed,
            PromptTokens = prompt,
            CompletionTokens = completion,
            OutputSummary = type.ToString()
        };
}
