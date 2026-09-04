using AiDevAgent.Application.Jira;
using AiDevAgent.Application.Workflows;
using AiDevAgent.Domain.Entities;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiDevAgent.Application.Tests;

public class JiraServiceTests
{
    [Fact]
    public void Status_Unconfigured_WithoutToken()
    {
        var service = CreateService(new FakeJira());
        var status = service.GetStatus();
        Assert.False(status.Configured);
        Assert.Contains("Jira:Token", status.Message);
    }

    [Fact]
    public async Task GetBoardWork_UsesActiveSprintAndBacklog()
    {
        var jira = new FakeJira
        {
            Boards = [new JiraBoardInfo(7, "Team board", "scrum", "PROJ")],
            Sprints = [new JiraSprintInfo(3, "Sprint 9", "active")],
            SprintIssues =
            [
                new JiraIssueSummary("PROJ-10", "1", "Do the thing", "To Do", "Story", "Medium", "Alex")
            ],
            Backlog =
            [
                new JiraIssueSummary("PROJ-11", "2", "Later", "To Do", "Task", null, null)
            ]
        };
        var service = CreateService(jira, configured: true);

        var work = await service.GetBoardWorkAsync(7);

        Assert.NotNull(work);
        Assert.Equal("Sprint 9", work.ActiveSprint?.Name);
        Assert.Equal("PROJ-10", Assert.Single(work.ActiveSprint!.Issues).Key);
        Assert.Equal("PROJ-11", Assert.Single(work.Backlog).Key);
        Assert.Empty(work.BoardIssues);
    }

    [Fact]
    public async Task StartWorkflow_CreatesTaskFromJiraDetails()
    {
        var project = new Project { Name = "P", GitLabProjectId = "1", RepositoryUrl = "https://example.invalid/repo.git" };
        var issue = new JiraIssueDetails(
            "PROJ-42",
            "42",
            "Add health check",
            "Return HTTP 200 from GET /health.",
            "To Do",
            "Story",
            "High",
            "Alex",
            ["api"],
            null,
            null,
            "https://example.atlassian.net/browse/PROJ-42",
            []);
        var jira = new FakeJira { Details = issue };
        var tasks = new RecordingTasks();
        var queue = new FakeQueue();
        var service = CreateService(jira, configured: true, project, tasks, queue);

        var workflow = await service.StartWorkflowAsync(project.Id, "PROJ-42");

        Assert.NotNull(workflow);
        var task = Assert.Single(tasks.Added);
        Assert.Equal("PROJ-42", task.JiraIssueKey);
        Assert.Equal("[PROJ-42] Add health check", task.Title);
        Assert.Contains("Return HTTP 200 from GET /health.", task.Description);
        Assert.Equal([workflow!.Id], queue.Enqueued);
    }

    private static JiraService CreateService(
        FakeJira jira,
        bool configured = false,
        Project? project = null,
        RecordingTasks? tasks = null,
        FakeQueue? queue = null)
    {
        project ??= new Project { Name = "P", GitLabProjectId = "1", RepositoryUrl = "https://example.invalid/repo.git" };
        tasks ??= new RecordingTasks();
        queue ??= new FakeQueue();

        var settings = configured
            ? new Dictionary<string, string?>
            {
                ["Jira:BaseUrl"] = "https://example.atlassian.net",
                ["Jira:Email"] = "dev@example.com",
                ["Jira:Token"] = "token",
                ["Jira:BoardId"] = "7"
            }
            : new Dictionary<string, string?>
            {
                ["Jira:BaseUrl"] = "https://example.atlassian.net"
            };
        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var workflowService = new WorkflowService(
            tasks,
            new FakeWorkflows(),
            queue,
            new FakeUow(),
            config,
            NullLogger<WorkflowService>.Instance);

        return new JiraService(
            jira,
            new FakeProjects(project),
            tasks,
            new FakeUow(),
            workflowService,
            config,
            NullLogger<JiraService>.Instance);
    }

    private sealed class FakeJira : IJiraClient
    {
        public List<JiraBoardInfo> Boards { get; init; } = [];
        public List<JiraSprintInfo> Sprints { get; init; } = [];
        public List<JiraIssueSummary> SprintIssues { get; init; } = [];
        public List<JiraIssueSummary> Backlog { get; init; } = [];
        public List<JiraIssueSummary> BoardIssues { get; init; } = [];
        public JiraIssueDetails? Details { get; init; }

        public Task<IReadOnlyList<JiraBoardInfo>> ListBoardsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<JiraBoardInfo>>(Boards);

        public Task<JiraBoardInfo?> GetBoardAsync(int boardId, CancellationToken cancellationToken = default)
            => Task.FromResult(Boards.FirstOrDefault(b => b.Id == boardId) ?? Boards.FirstOrDefault());

        public Task<IReadOnlyList<JiraSprintInfo>> ListActiveSprintsAsync(int boardId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<JiraSprintInfo>>(Sprints);

        public Task<IReadOnlyList<JiraIssueSummary>> ListSprintIssuesAsync(int sprintId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<JiraIssueSummary>>(SprintIssues);

        public Task<IReadOnlyList<JiraIssueSummary>> ListBacklogIssuesAsync(int boardId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<JiraIssueSummary>>(Backlog);

        public Task<IReadOnlyList<JiraIssueSummary>> ListBoardIssuesAsync(int boardId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<JiraIssueSummary>>(BoardIssues);

        public Task<JiraIssueDetails?> GetIssueAsync(string issueKey, CancellationToken cancellationToken = default)
            => Task.FromResult(Details is not null && Details.Key == issueKey ? Details : Details);
    }

    private sealed class RecordingTasks : IDevTaskRepository
    {
        public List<DevTask> Added { get; } = [];

        public Task<DevTask?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Added.FirstOrDefault(t => t.Id == id));

        public Task<DevTask?> GetByProjectAndJiraKeyAsync(Guid projectId, string jiraIssueKey, CancellationToken cancellationToken = default)
            => Task.FromResult(Added.FirstOrDefault(t => t.ProjectId == projectId && t.JiraIssueKey == jiraIssueKey));

        public Task<IReadOnlyList<DevTask>> ListByProjectAsync(Guid projectId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DevTask>>(Added.Where(t => t.ProjectId == projectId).ToList());

        public Task AddAsync(DevTask task, CancellationToken cancellationToken = default)
        {
            Added.Add(task);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProjects(Project project) : IProjectRepository
    {
        public Task<Project?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Project?>(project.Id == id ? project : null);

        public Task<IReadOnlyList<Project>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Project>>([project]);

        public Task AddAsync(Project p, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyList<Guid>?> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Guid>?>([]);
    }

    private sealed class FakeWorkflows : IWorkflowRepository
    {
        private readonly List<Workflow> _items = [];

        public Task<Workflow?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(_items.FirstOrDefault(w => w.Id == id));

        public Task<Workflow?> GetByIdWithDetailsAsync(Guid id, CancellationToken cancellationToken = default)
            => GetByIdAsync(id, cancellationToken);

        public Task<IReadOnlyList<Workflow>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Workflow>>(_items);

        public Task<IReadOnlyList<Workflow>> ListPendingAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Workflow>>([]);

        public Task AddAsync(Workflow workflow, CancellationToken cancellationToken = default)
        {
            _items.Add(workflow);
            return Task.CompletedTask;
        }

        public Task AddEventAsync(WorkflowEvent workflowEvent, CancellationToken cancellationToken = default)
        {
            var workflow = _items.FirstOrDefault(w => w.Id == workflowEvent.WorkflowId);
            workflow?.Events.Add(workflowEvent);
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
            => Task.FromResult(false);

        public Task<IReadOnlyList<string>> ListUserCommandsAsync(Guid workflowId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<string>>([]);
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
