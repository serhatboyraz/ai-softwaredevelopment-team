using System.ClientModel;
using AiDevAgent.Application.AI;
using AiDevAgent.Domain.Interfaces;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiDevAgent.Infrastructure.AI;

public sealed class AzureOpenAiRuntime : IAiRuntime, IDisposable
{
    private readonly IChatClient _client;

    public AzureOpenAiRuntime(
        IOptions<AiOptions> optionsAccessor,
        ISecretProvider secrets,
        IConfiguration configuration,
        ILoggerFactory loggerFactory)
    {
        var options = optionsAccessor.Value;
        Provider = string.IsNullOrWhiteSpace(options.Provider) ? "AzureOpenAI" : options.Provider;
        Deployment = options.Deployment;
        IsConfigured = options.IsConfigured &&
            string.Equals(Provider, "AzureOpenAI", StringComparison.OrdinalIgnoreCase);

        if (!IsConfigured)
        {
            _client = new UnconfiguredChatClient();
            return;
        }

        var apiKey = secrets.GetSecretAsync("AI:ApiKey").GetAwaiter().GetResult()
            ?? options.ApiKey
            ?? configuration["AI:ApiKey"]
            ?? Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY");

        var endpoint = NormalizeAzureEndpoint(options.Endpoint);
        var azure = string.IsNullOrWhiteSpace(apiKey)
            ? new AzureOpenAIClient(endpoint, new DefaultAzureCredential())
            : new AzureOpenAIClient(endpoint, new ApiKeyCredential(apiKey));

        IChatClient inner = azure.GetChatClient(options.Deployment).AsIChatClient();
        _client = new ChatClientBuilder(inner)
            .UseLogging(loggerFactory)
            .UseFunctionInvocation()
            .Build();
    }

    public bool IsConfigured { get; }
    public string Provider { get; }
    public string Deployment { get; }

    public IChatClient ChatClient => _client;

    public void Dispose() => _client.Dispose();

    /// <summary>
    /// AzureOpenAIClient wants the resource root (https://{name}.openai.azure.com/),
    /// not the /openai/v1 path used by the OpenAI-compatible REST surface.
    /// </summary>
    internal static Uri NormalizeAzureEndpoint(string endpoint)
    {
        var uri = new Uri(endpoint);
        var path = uri.AbsolutePath.TrimEnd('/');
        if (path.Length == 0 || path == "/" ||
            path.Equals("/openai/v1", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/openai", StringComparison.OrdinalIgnoreCase))
            return new Uri($"{uri.Scheme}://{uri.Authority}/");

        return uri;
    }
}

internal sealed class UnconfiguredChatClient : IChatClient
{
    public ChatClientMetadata Metadata { get; } = new("unconfigured");

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "Azure OpenAI is not configured. Set AI:Endpoint and AI:Deployment (and AI:ApiKey or use DefaultAzureCredential).");

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            "Azure OpenAI is not configured. Set AI:Endpoint and AI:Deployment (and AI:ApiKey or use DefaultAzureCredential).");

    public object? GetService(Type serviceType, object? serviceKey = null)
        => serviceType == typeof(ChatClientMetadata) ? Metadata : null;

    public void Dispose()
    {
    }
}
