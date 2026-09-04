using AiDevAgent.Application.Jira;
using AiDevAgent.Application.Workflows;
using AiDevAgent.Domain.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace AiDevAgent.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<ProjectService>();
        services.AddScoped<TaskService>();
        services.AddScoped<WorkflowService>();
        services.AddScoped<JiraService>();
        services.AddSingleton<ITaskStepArtifactWriter, TaskStepMarkdownWriter>();
        services.AddScoped<IWorkflowExecutor, AgentWorkflowExecutor>();
        return services;
    }
}
