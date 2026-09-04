using AiDevAgent.Agents.Analysis;
using AiDevAgent.Agents.Context;
using AiDevAgent.Agents.Development;
using AiDevAgent.Agents.Git;
using AiDevAgent.Agents.Prompts;
using AiDevAgent.Agents.Support;
using AiDevAgent.Agents.Testing;
using AiDevAgent.Application.Agents;
using Microsoft.Extensions.DependencyInjection;

namespace AiDevAgent.Agents;

public static class DependencyInjection
{
    public static IServiceCollection AddAgents(this IServiceCollection services)
    {
        services.AddSingleton<IPromptStore, FilePromptStore>();
        services.AddSingleton<LlmAgentRunner>();
        services.AddScoped<IContextAgent, ContextAgent>();
        services.AddScoped<IAnalysisAgent, AnalysisAgent>();
        services.AddScoped<IDevelopmentAgent, DevelopmentAgent>();
        services.AddScoped<ITestingAgent, TestingAgent>();
        services.AddScoped<IGitAgent, GitAgent>();
        return services;
    }
}
