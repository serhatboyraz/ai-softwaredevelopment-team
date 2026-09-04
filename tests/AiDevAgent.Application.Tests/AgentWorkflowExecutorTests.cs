using AiDevAgent.Application.Agents;
using AiDevAgent.Application.Contracts;
using AiDevAgent.Application.Workflows;
using AiDevAgent.Domain.Entities;
using AiDevAgent.Domain.Enums;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using DomainTaskStatus = AiDevAgent.Domain.Enums.TaskStatus;

namespace AiDevAgent.Application.Tests;

public class AgentWorkflowExecutorTests
{
    [Fact]
    public async Task WaitingForInformation_WhenContextNotReady()
    {
        var task = new DevTask { Title = "X", Description = "short" };
        var workflow = new Workflow { TaskId = task.Id, State = WorkflowState.Pending };
        var harness = new Harness(workflow, task, new ContextResult(false, 0.1, ["criteria"], [], ["Need AC"]));

        await harness.Executor.ExecuteAsync(workflow.Id);

        Assert.Equal(WorkflowState.WaitingForInformation, workflow.State);
        Assert.Equal(DomainTaskStatus.WaitingForUser, task.Status);
        Assert.Contains(harness.Workflows.Events, e => e.EventType == "agent.completed");
    }

    [Fact]
    public async Task Completes_WhenAgentsSucceed()
    {
        var task = new DevTask { Title = "Add health check", Description = "Return 200 from GET /health with a JSON body." };
        var workflow = new Workflow { TaskId = task.Id, State = WorkflowState.Pending, MaxRepairAttempts = 3 };
        var harness = new Harness(workflow, task, new ContextResult(true, 0.9, [], ["README.md"], []));

        await harness.Executor.ExecuteAsync(workflow.Id);

        Assert.Equal(WorkflowState.Completed, workflow.State);
        Assert.Equal(DomainTaskStatus.Completed, task.Status);
        Assert.NotNull(workflow.BranchName);
        Assert.NotNull(harness.Workflows.MergeRequest);
    }

    [Fact]
    public async Task WaitsForApproval_WhenConfigured()
    {
        var task = new DevTask { Title = "Add health check", Description = "Return 200 from GET /health with a JSON body." };
        var workflow = new Workflow { TaskId = task.Id, State = WorkflowState.Pending, MaxRepairAttempts = 3 };
        var harness = new Harness(
            workflow,
            task,
            new ContextResult(true, 0.9, [], [], []),
            requireApproval: true);

        await harness.Executor.ExecuteAsync(workflow.Id);

        Assert.Equal(WorkflowState.WaitingForApproval, workflow.State);
        Assert.Single(harness.Workflows.Approvals);
    }

    [Fact]
    public async Task Resumes_FromDeveloping_WithoutReRunningEarlierAgents()
    {
        var task = new DevTask { Title = "Add health check", Description = "Return 200 from GET /health with a JSON body." };
        var workflow = new Workflow
        {
            TaskId = task.Id,
            State = WorkflowState.Developing,
            AnalysisResultJson = "{}",
            MaxRepairAttempts = 3
        };
        var harness = new Harness(workflow, task, new ContextResult(true, 0.9, [], ["README.md"], []));

        await harness.Executor.ExecuteAsync(workflow.Id);

        Assert.Equal(WorkflowState.Completed, workflow.State);
        Assert.DoesNotContain(harness.Workflows.Events, e => e.EventType == "workflow.started");
    }

    [Fact]
    public async Task FailsImmediately_WhenSandboxPackageFeedUnavailable()
    {
        var task = new DevTask { Title = "Create An Api", Description = "Return HTTP 200 from GET /health with a JSON status payload." };
        var workflow = new Workflow { TaskId = task.Id, State = WorkflowState.Pending, MaxRepairAttempts = 3 };
        var development = new CountingDevelopment();
        var harness = new Harness(
            workflow,
            task,
            new ContextResult(true, 0.9, [], ["README.md"], []),
            development: development,
            testing: new FakeTesting(new TestResult(
                false,
                false,
                0,
                1,
                ["Sandbox NuGet restore failed (NU1301)."],
                InfrastructureFailure: true),
                Summary: "Build failed: NuGet restore (NU1301) — sandbox could not reach the package feed."));

        await harness.Executor.ExecuteAsync(workflow.Id);

        Assert.Equal(WorkflowState.Failed, workflow.State);
        Assert.Equal(1, development.Calls);
        Assert.Contains(harness.Workflows.Events, e => e.EventType == "workflow.failed");
        Assert.Contains("NU1301", workflow.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Repairs_WhenTestsFailWithoutInfrastructureError()
    {
        var task = new DevTask { Title = "Add health check", Description = "Return 200 from GET /health with a JSON body." };
        var workflow = new Workflow { TaskId = task.Id, State = WorkflowState.Pending, MaxRepairAttempts = 1 };
        var development = new CountingDevelopment();
        var harness = new Harness(
            workflow,
            task,
            new ContextResult(true, 0.9, [], ["README.md"], []),
            development: development,
            testing: new FakeTesting(new TestResult(false, true, 0, 1, ["Assertion failed."]), Summary: "Tests failed."));

        await harness.Executor.ExecuteAsync(workflow.Id);

        Assert.Equal(WorkflowState.Failed, workflow.State);
        Assert.Equal(2, development.Calls);
        Assert.Contains("repair attempt", workflow.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class Harness
    {
        public FakeWorkflows Workflows { get; }
        public AgentWorkflowExecutor Executor { get; }

        public Harness(
            Workflow workflow,
            DevTask task,
            ContextResult context,
            bool requireApproval = false,
            IDevelopmentAgent? development = null,
            ITestingAgent? testing = null)
        {
            Workflows = new FakeWorkflows(workflow);
            var tasks = new FakeTasks(task);
            var projects = new FakeProjects(new Project { Id = task.ProjectId, Name = "p", GitLabProjectId = "1", RepositoryUrl = "https://example.invalid/repo.git" });
            var workspaces = new FakeWorkspaces();
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Workflow:EnableGitClone"] = "false",
                ["Workflow:RequireApprovalBeforeMr"] = requireApproval ? "true" : "false"
            }).Build();

            Executor = new AgentWorkflowExecutor(
                Workflows,
                tasks,
                projects,
                new FakeUow(),
                new FakeEvents(),
                workspaces,
                new FakeGit(),
                new FakeSecrets(),
                new FakeContext(context),
                new FakeAnalysis(),
                development ?? new CountingDevelopment(),
                testing ?? new FakeTesting(new TestResult(true, true, 1, 0, []), Summary: "passed"),
                new FakeGitAgent(),
                new FakeArtifacts(),
                config,
                NullLogger<AgentWorkflowExecutor>.Instance);
        }
    }

    private sealed class FakeWorkflows(Workflow workflow) : IWorkflowRepository
    {
        public List<WorkflowEvent> Events { get; } = [];
        public MergeRequest? MergeRequest { get; private set; }
        public List<ApprovalRequest> Approvals { get; } = [];

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
        {
            MergeRequest = mergeRequest;
            workflow.MergeRequest = mergeRequest;
            return Task.CompletedTask;
        }

        public Task AddAgentRunAsync(AgentRun agentRun, CancellationToken cancellationToken = default)
        {
            workflow.AgentRuns.Add(agentRun);
            return Task.CompletedTask;
        }
        public Task AddTestRunAsync(TestRun testRun, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task AddApprovalRequestAsync(ApprovalRequest approvalRequest, CancellationToken cancellationToken = default)
        {
            Approvals.Add(approvalRequest);
            workflow.ApprovalRequests.Add(approvalRequest);
            return Task.CompletedTask;
        }

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

    private sealed class FakeProjects(Project project) : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Project?>(project);

        public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Project>>([project]);

        public Task AddAsync(Project p, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Guid>?> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Guid>?>([]);
    }

    private sealed class FakeUow : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
    }

    private sealed class FakeEvents : IWorkflowEventPublisher
    {
        public Task PublishAsync(Guid workflowId, string eventType, string? message = null, object? payload = null, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeWorkspaces : IWorkspaceManager
    {
        public Task<string> EnsureWorkspaceAsync(Guid workflowId, string repositoryUrl, string? branch, CancellationToken cancellationToken = default)
            => Task.FromResult(Path.Combine(Path.GetTempPath(), "aidevagent-test", workflowId.ToString("N")));

        public string GetWorkspacePath(Guid workflowId) => Path.Combine(Path.GetTempPath(), workflowId.ToString("N"));
        public Task CleanupAsync(Guid workflowId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeGit : IGitService
    {
        public Task CloneAsync(string repositoryUrl, string workspacePath, string? branch, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task CreateBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> StatusAsync(string workspacePath, CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task<string> DiffAsync(string workspacePath, CancellationToken cancellationToken = default) => Task.FromResult("");
        public Task CommitAsync(string workspacePath, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PushAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> GetHeadShaAsync(string workspacePath, CancellationToken cancellationToken = default) => Task.FromResult("abc");
        public Task<string> CheckoutTargetBranchAsync(string workspacePath, string branchName, CancellationToken cancellationToken = default)
            => Task.FromResult(branchName);
        public Task<string?> GetRemoteDefaultBranchAsync(string workspacePath, CancellationToken cancellationToken = default)
            => Task.FromResult<string?>("main");
    }

    private sealed class FakeArtifacts : ITaskStepArtifactWriter
    {
        public Task WriteAsync(string workspacePath, string taskTitle, AgentType agentType, string? summary, object result, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeSecrets : ISecretProvider
    {
        public Task<string?> GetSecretAsync(string name, CancellationToken cancellationToken = default) => Task.FromResult<string?>(null);
    }

    private sealed class FakeContext(ContextResult result) : IContextAgent
    {
        public Task<AgentOutcome<ContextResult>> RunAsync(AgentWorkItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentOutcome<ContextResult>(result, Summary: result.Ready ? "ready" : "need info"));
    }

    private sealed class FakeAnalysis : IAnalysisAgent
    {
        public Task<AgentOutcome<AnalysisResult>> RunAsync(AgentWorkItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentOutcome<AnalysisResult>(
                new AnalysisResult("plan", "none", [], ["step"], [], ["test"]),
                Summary: "plan"));
    }

    private sealed class CountingDevelopment : IDevelopmentAgent
    {
        public int Calls { get; private set; }

        public Task<AgentOutcome<DevelopmentResult>> RunAsync(AgentWorkItem item, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(new AgentOutcome<DevelopmentResult>(
                new DevelopmentResult(true, [], "changed", []),
                Summary: "changed"));
        }
    }

    private sealed class FakeTesting(TestResult result, string? Summary = null) : ITestingAgent
    {
        public Task<AgentOutcome<TestResult>> RunAsync(AgentWorkItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentOutcome<TestResult>(result, Summary: Summary ?? "testing"));
    }

    private sealed class FakeGitAgent : IGitAgent
    {
        public Task<AgentOutcome<GitExecutionResult>> RunAsync(GitWorkItem item, CancellationToken cancellationToken = default)
            => Task.FromResult(new AgentOutcome<GitExecutionResult>(
                new GitExecutionResult(true, "ai/task-1-add-health-check", "abc123", "https://gitlab.example/mr/1", 1, "MR", "desc", "opened"),
                Summary: "mr"));
    }
}
