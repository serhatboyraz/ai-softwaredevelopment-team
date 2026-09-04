using AiDevAgent.Application.Workflows;
using AiDevAgent.Domain.Entities;
using AiDevAgent.Domain.Enums;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using DomainTaskStatus = AiDevAgent.Domain.Enums.TaskStatus;

namespace AiDevAgent.Application.Tests;

public class WorkflowServiceTests
{
    [Fact]
    public async Task Rerun_Failed_ResumesFromDeveloping_WhenAnalysisExists()
    {
        var task = new DevTask { Title = "T", Description = "D", Status = DomainTaskStatus.Failed };
        var workflow = new Workflow
        {
            TaskId = task.Id,
            State = WorkflowState.Failed,
            AnalysisResultJson = "{}",
            ErrorMessage = "boom",
            CompletedAt = DateTimeOffset.UtcNow,
            RepairAttempt = 3,
            MaxRepairAttempts = 3
        };
        var harness = new Harness(workflow, task);

        var result = await harness.Service.RerunAsync(workflow.Id);

        Assert.True(result.Accepted);
        Assert.Equal(WorkflowState.Developing, workflow.State);
        Assert.Null(workflow.ErrorMessage);
        Assert.Null(workflow.CompletedAt);
        Assert.Equal(0, workflow.RepairAttempt);
        Assert.Equal(DomainTaskStatus.Queued, task.Status);
        Assert.Equal([workflow.Id], harness.Queue.Enqueued);
        Assert.Contains(harness.Workflows.Events, e => e.EventType == "workflow.rerun");
    }

    [Fact]
    public async Task Rerun_WaitingForInformation_ContinuesAtAnalyzing()
    {
        var task = new DevTask { Title = "T", Description = "D", Status = DomainTaskStatus.WaitingForUser };
        var workflow = new Workflow
        {
            TaskId = task.Id,
            State = WorkflowState.WaitingForInformation,
            ContextResultJson = "{}"
        };
        var harness = new Harness(workflow, task);

        var result = await harness.Service.RerunAsync(workflow.Id);

        Assert.True(result.Accepted);
        Assert.Equal(WorkflowState.Analyzing, workflow.State);
        Assert.Equal([workflow.Id], harness.Queue.Enqueued);
    }

    [Fact]
    public async Task Rerun_StuckDeveloping_RequeuesSameCheckpoint()
    {
        var task = new DevTask { Title = "T", Description = "D", Status = DomainTaskStatus.InProgress };
        var workflow = new Workflow { TaskId = task.Id, State = WorkflowState.Developing };
        var harness = new Harness(workflow, task);

        var result = await harness.Service.RerunAsync(workflow.Id);

        Assert.True(result.Accepted);
        Assert.Equal(WorkflowState.Developing, workflow.State);
        Assert.Equal([workflow.Id], harness.Queue.Enqueued);
    }

    [Fact]
    public async Task Rerun_Completed_IsRejected()
    {
        var task = new DevTask { Title = "T", Description = "D", Status = DomainTaskStatus.Completed };
        var workflow = new Workflow { TaskId = task.Id, State = WorkflowState.Completed };
        var harness = new Harness(workflow, task);

        var result = await harness.Service.RerunAsync(workflow.Id);

        Assert.True(result.Found);
        Assert.False(result.Accepted);
        Assert.Empty(harness.Queue.Enqueued);
        Assert.Equal(WorkflowState.Completed, workflow.State);
    }

    [Fact]
    public async Task GetDetail_IncludesStepProgressAndAgentRuns()
    {
        var started = DateTimeOffset.Parse("2026-09-04T12:00:00Z");
        var task = new DevTask { Title = "T", Description = "D" };
        var workflow = new Workflow { TaskId = task.Id, State = WorkflowState.Analyzing };
        workflow.AgentRuns.Add(new AgentRun
        {
            AgentType = AgentType.Context,
            Success = true,
            StartedAt = started,
            CompletedAt = started.AddSeconds(8),
            PromptTokens = 40,
            CompletionTokens = 12,
            OutputSummary = "Context ready"
        });
        var harness = new Harness(workflow, task);

        var detail = await harness.Service.GetDetailAsync(workflow.Id);

        Assert.NotNull(detail);
        Assert.Single(detail.AgentRuns);
        Assert.Equal(52, detail.Steps.Single(s => s.Key == "context").TotalTokens);
        Assert.Equal("completed", detail.Steps.Single(s => s.Key == "context").Status);
        Assert.Equal("running", detail.Steps.Single(s => s.Key == "analysis").Status);
    }

    [Fact]
    public async Task Rerun_WaitingForApproval_IsRejected()
    {
        var task = new DevTask { Title = "T", Description = "D" };
        var workflow = new Workflow { TaskId = task.Id, State = WorkflowState.WaitingForApproval };
        var harness = new Harness(workflow, task);

        var result = await harness.Service.RerunAsync(workflow.Id);

        Assert.False(result.Accepted);
        Assert.Empty(harness.Queue.Enqueued);
    }

    [Fact]
    public async Task Rerun_MissingWorkflow_IsNotFound()
    {
        var harness = new Harness(new Workflow { Id = Guid.NewGuid() }, new DevTask());

        var result = await harness.Service.RerunAsync(Guid.NewGuid());

        Assert.False(result.Found);
        Assert.Empty(harness.Queue.Enqueued);
    }

    [Fact]
    public async Task SendCommand_AppendsAndRequeuesDevelopmentImmediately()
    {
        var task = new DevTask { Title = "T", Description = "D", Status = DomainTaskStatus.Failed };
        var workflow = new Workflow
        {
            TaskId = task.Id,
            State = WorkflowState.Failed,
            AnalysisResultJson = "{}",
            CompletedAt = DateTimeOffset.UtcNow
        };
        var harness = new Harness(workflow, task);

        var result = await harness.Service.SendCommandAsync(workflow.Id, "  also add logging  ");

        Assert.True(result.Accepted);
        Assert.Equal(WorkflowState.Developing, workflow.State);
        Assert.Null(workflow.CompletedAt);
        Assert.Equal(DomainTaskStatus.Queued, task.Status);
        Assert.Equal([workflow.Id], harness.Queue.Enqueued);
        Assert.Contains(harness.Workflows.Events, e => e.EventType == "human.command" && e.Message == "also add logging");
    }

    [Fact]
    public async Task SendCommand_Completed_ResumesDeveloping()
    {
        var task = new DevTask { Title = "T", Description = "D", Status = DomainTaskStatus.Completed };
        var workflow = new Workflow
        {
            TaskId = task.Id,
            State = WorkflowState.Completed,
            AnalysisResultJson = "{}"
        };
        var harness = new Harness(workflow, task);

        var result = await harness.Service.SendCommandAsync(workflow.Id, "rename the endpoint to /ready");

        Assert.True(result.Accepted);
        Assert.Equal(WorkflowState.Developing, workflow.State);
        Assert.Equal([workflow.Id], harness.Queue.Enqueued);
    }

    [Fact]
    public async Task SendCommand_Empty_IsRejected()
    {
        var task = new DevTask { Title = "T", Description = "D" };
        var workflow = new Workflow { TaskId = task.Id, State = WorkflowState.Developing };
        var harness = new Harness(workflow, task);

        var result = await harness.Service.SendCommandAsync(workflow.Id, "   ");

        Assert.False(result.Accepted);
        Assert.Empty(harness.Queue.Enqueued);
    }

    private sealed class Harness
    {
        public FakeWorkflows Workflows { get; }
        public FakeQueue Queue { get; } = new();
        public WorkflowService Service { get; }

        public Harness(Workflow workflow, DevTask task)
        {
            Workflows = new FakeWorkflows(workflow);
            var config = new ConfigurationBuilder().AddInMemoryCollection().Build();
            Service = new WorkflowService(
                new FakeTasks(task),
                Workflows,
                Queue,
                new FakeUow(),
                config,
                NullLogger<WorkflowService>.Instance);
        }
    }

    private sealed class FakeWorkflows(Workflow workflow) : IWorkflowRepository
    {
        public List<WorkflowEvent> Events { get; } = [];

        public Task<Workflow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Workflow?>(workflow.Id == id ? workflow : null);

        public Task<Workflow?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
            => GetByIdAsync(id, cancellationToken);

        public Task<IReadOnlyList<Workflow>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Workflow>>([workflow]);

        public Task<IReadOnlyList<Workflow>> ListPendingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Workflow>>([]);

        public Task AddAsync(Workflow w, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(workflowEvent);
            workflow.Events.Add(workflowEvent);
            return Task.CompletedTask;
        }

        public Task AddMergeRequestAsync(MergeRequest mergeRequest, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddAgentRunAsync(AgentRun agentRun, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddTestRunAsync(TestRun testRun, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task AddApprovalRequestAsync(ApprovalRequest approvalRequest, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> IsCancelledAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(workflow.State == WorkflowState.Cancelled);

        public Task<IReadOnlyList<string>> ListUserCommandsAsync(Guid workflowId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>(
                Events.Where(e => e.EventType == "human.command" && e.Message is not null).Select(e => e.Message!).ToList());
    }

    private sealed class FakeTasks(DevTask task) : IDevTaskRepository
    {
        public Task<DevTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<DevTask?>(task.Id == id ? task : null);

        public Task<DevTask?> GetByProjectAndJiraKeyAsync(Guid projectId, string jiraIssueKey, CancellationToken cancellationToken = default)
            => Task.FromResult<DevTask?>(null);

        public Task<IReadOnlyList<DevTask>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DevTask>>([task]);

        public Task AddAsync(DevTask t, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeQueue : IWorkflowQueue
    {
        public List<Guid> Enqueued { get; } = [];

        public ValueTask EnqueueAsync(Guid workflowId, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(workflowId);
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }

    private sealed class FakeUow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }
}
