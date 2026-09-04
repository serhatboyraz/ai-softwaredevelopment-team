using AiDevAgent.Application.AI;
using AiDevAgent.Domain.Interfaces;
using AiDevAgent.Infrastructure.AI;
using AiDevAgent.Infrastructure.Docker;
using AiDevAgent.Infrastructure.Git;
using AiDevAgent.Infrastructure.GitLab;
using AiDevAgent.Infrastructure.Jira;
using AiDevAgent.Infrastructure.Persistence;
using AiDevAgent.Infrastructure.Persistence.Repositories;
using AiDevAgent.Infrastructure.Repository;
using AiDevAgent.Infrastructure.Secrets;
using AiDevAgent.Infrastructure.WorkflowRuntime;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiDevAgent.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration.GetValue<string>("Database:Provider") ?? "Postgres";
        var connectionString = configuration.GetConnectionString("Default")
            ?? "Host=localhost;Port=5432;Database=aidevagent;Username=aidevagent;Password=aidevagent";

        services.AddDbContext<AppDbContext>(options =>
        {
            if (string.Equals(provider, "Sqlite", StringComparison.OrdinalIgnoreCase))
                options.UseSqlite(connectionString);
            else
                options.UseNpgsql(connectionString);
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IDevTaskRepository, DevTaskRepository>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();

        services.AddSingleton<IWorkflowQueue, InMemoryWorkflowQueue>();
        services.AddSingleton<IWorkflowEventPublisher, SignalRWorkflowEventPublisher>();
        services.AddHostedService<WorkflowBackgroundService>();

        services.AddSingleton<ISecretProvider, ConfigurationSecretProvider>();
        services.AddSingleton<IWorkspaceManager, WorkspaceManager>();
        services.AddSingleton<IGitService, GitCliService>();
        services.AddSingleton<IRepositoryTools, FileSystemRepositoryTools>();
        services.AddSingleton<ISandboxExecutor, DockerSandboxExecutor>();

        services.Configure<AiOptions>(configuration.GetSection(AiOptions.SectionName));
        services.AddSingleton<AzureOpenAiRuntime>();
        services.AddSingleton<IAiRuntime>(sp => sp.GetRequiredService<AzureOpenAiRuntime>());
        services.AddSingleton<IChatClient>(sp => sp.GetRequiredService<AzureOpenAiRuntime>().ChatClient);

        var gitlabBase = configuration["GitLab:BaseUrl"] ?? "https://gitlab.com";
        services.AddHttpClient<IGitLabClient, GitLabClient>(client =>
        {
            client.BaseAddress = new Uri(gitlabBase.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        var jiraBase = configuration["Jira:BaseUrl"];
        if (string.IsNullOrWhiteSpace(jiraBase))
            jiraBase = "https://jira.invalid";
        services.AddHttpClient<IJiraClient, JiraClient>(client =>
        {
            client.BaseAddress = new Uri(jiraBase.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromMinutes(2);
        });

        return services;
    }
}
