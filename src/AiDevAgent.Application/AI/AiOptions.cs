namespace AiDevAgent.Application.AI;

public sealed class AiOptions
{
    public const string SectionName = "AI";

    public string Provider { get; set; } = "AzureOpenAI";
    public string Deployment { get; set; } = "coding-model";
    public string Endpoint { get; set; } = string.Empty;
    public string? ApiKey { get; set; }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Deployment);
}

/// <summary>Application-level AI availability. Provider SDKs stay in Infrastructure.</summary>
public interface IAiRuntime
{
    bool IsConfigured { get; }
    string Provider { get; }
    string Deployment { get; }
}
