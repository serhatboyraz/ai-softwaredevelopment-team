using AiDevAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AiDevAgent.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Project> Projects => Set<Project>();
    public DbSet<DevTask> Tasks => Set<DevTask>();
    public DbSet<Workflow> Workflows => Set<Workflow>();
    public DbSet<WorkflowStep> WorkflowSteps => Set<WorkflowStep>();
    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();
    public DbSet<ToolCall> ToolCalls => Set<ToolCall>();
    public DbSet<TestRun> TestRuns => Set<TestRun>();
    public DbSet<Artifact> Artifacts => Set<Artifact>();
    public DbSet<MergeRequest> MergeRequests => Set<MergeRequest>();
    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Project>(e =>
        {
            e.ToTable("projects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.GitLabProjectId).HasMaxLength(100).IsRequired();
            e.Property(x => x.RepositoryUrl).HasMaxLength(500).IsRequired();
            e.Property(x => x.DefaultBranch).HasMaxLength(200);
            e.HasIndex(x => x.GitLabProjectId);
        });

        modelBuilder.Entity<DevTask>(e =>
        {
            e.ToTable("tasks");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Description).IsRequired();
            e.Property(x => x.JiraIssueKey).HasMaxLength(64);
            e.Property(x => x.JiraIssueUrl).HasMaxLength(500);
            e.HasIndex(x => x.JiraIssueKey);
            e.HasOne(x => x.Project).WithMany(x => x.Tasks).HasForeignKey(x => x.ProjectId);
        });

        modelBuilder.Entity<Workflow>(e =>
        {
            e.ToTable("workflows");
            e.HasKey(x => x.Id);
            e.Property(x => x.CorrelationId).HasMaxLength(64).IsRequired();
            e.Property(x => x.BranchName).HasMaxLength(200);
            e.Property(x => x.CommitSha).HasMaxLength(100);
            e.HasIndex(x => x.State);
            e.HasOne(x => x.Task).WithMany(x => x.Workflows).HasForeignKey(x => x.TaskId);
            e.HasOne(x => x.MergeRequest).WithOne(x => x.Workflow).HasForeignKey<MergeRequest>(x => x.WorkflowId);
        });

        modelBuilder.Entity<WorkflowStep>(e =>
        {
            e.ToTable("workflow_steps");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.HasOne(x => x.Workflow).WithMany(x => x.Steps).HasForeignKey(x => x.WorkflowId);
        });

        modelBuilder.Entity<WorkflowEvent>(e =>
        {
            e.ToTable("workflow_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.EventType).HasMaxLength(100).IsRequired();
            e.HasOne(x => x.Workflow).WithMany(x => x.Events).HasForeignKey(x => x.WorkflowId);
            e.HasIndex(x => new { x.WorkflowId, x.OccurredAt });
        });

        modelBuilder.Entity<AgentRun>(e =>
        {
            e.ToTable("agent_runs");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Workflow).WithMany(x => x.AgentRuns).HasForeignKey(x => x.WorkflowId);
        });

        modelBuilder.Entity<ToolCall>(e =>
        {
            e.ToTable("tool_calls");
            e.HasKey(x => x.Id);
            e.Property(x => x.ToolName).HasMaxLength(100).IsRequired();
            e.HasOne(x => x.Workflow).WithMany(x => x.ToolCalls).HasForeignKey(x => x.WorkflowId);
        });

        modelBuilder.Entity<TestRun>(e =>
        {
            e.ToTable("test_runs");
            e.HasKey(x => x.Id);
            e.HasOne(x => x.Workflow).WithMany(x => x.TestRuns).HasForeignKey(x => x.WorkflowId);
        });

        modelBuilder.Entity<Artifact>(e =>
        {
            e.ToTable("artifacts");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(100).IsRequired();
            e.Property(x => x.Path).HasMaxLength(1000).IsRequired();
            e.HasOne(x => x.Workflow).WithMany(x => x.Artifacts).HasForeignKey(x => x.WorkflowId);
        });

        modelBuilder.Entity<MergeRequest>(e =>
        {
            e.ToTable("merge_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(300).IsRequired();
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.State).HasMaxLength(50);
        });

        modelBuilder.Entity<ApprovalRequest>(e =>
        {
            e.ToTable("approval_requests");
            e.HasKey(x => x.Id);
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.HasOne(x => x.Workflow).WithMany(x => x.ApprovalRequests).HasForeignKey(x => x.WorkflowId);
        });
    }

    /// <summary>
    /// EnsureCreated does not add columns to existing databases. Patch Jira fields for already-created stores.
    /// </summary>
    public async Task EnsureRuntimeSchemaAsync(CancellationToken cancellationToken = default)
    {
        var provider = Database.ProviderName ?? string.Empty;
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await Database.ExecuteSqlRawAsync(
                """ALTER TABLE tasks ADD COLUMN IF NOT EXISTS "JiraIssueKey" character varying(64)""",
                cancellationToken);
            await Database.ExecuteSqlRawAsync(
                """ALTER TABLE tasks ADD COLUMN IF NOT EXISTS "JiraIssueUrl" character varying(500)""",
                cancellationToken);
            return;
        }

        if (provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase))
        {
            await TrySqliteAddColumnAsync("JiraIssueKey", cancellationToken);
            await TrySqliteAddColumnAsync("JiraIssueUrl", cancellationToken);
        }
    }

    private async Task TrySqliteAddColumnAsync(string column, CancellationToken cancellationToken)
    {
        var connection = Database.GetDbConnection();
        var shouldClose = connection.State != System.Data.ConnectionState.Open;
        if (shouldClose)
            await Database.OpenConnectionAsync(cancellationToken);

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA table_info(tasks)";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return;
            }
        }
        finally
        {
            if (shouldClose)
                await Database.CloseConnectionAsync();
        }

        if (column == "JiraIssueKey")
            await Database.ExecuteSqlRawAsync("""ALTER TABLE tasks ADD COLUMN "JiraIssueKey" TEXT""", cancellationToken);
        else if (column == "JiraIssueUrl")
            await Database.ExecuteSqlRawAsync("""ALTER TABLE tasks ADD COLUMN "JiraIssueUrl" TEXT""", cancellationToken);
    }
}
