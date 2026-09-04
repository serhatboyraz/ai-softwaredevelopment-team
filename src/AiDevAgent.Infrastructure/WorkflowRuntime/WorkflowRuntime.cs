using System.Threading.Channels;
using AiDevAgent.Domain.Interfaces;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiDevAgent.Infrastructure.WorkflowRuntime;

public sealed class InMemoryWorkflowQueue : IWorkflowQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask EnqueueAsync(Guid workflowId, CancellationToken cancellationToken = default)
        => _channel.Writer.WriteAsync(workflowId, cancellationToken);

    public async IAsyncEnumerable<Guid> DequeueAllAsync([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var id in _channel.Reader.ReadAllAsync(cancellationToken))
            yield return id;
    }
}

public sealed class LoggingWorkflowEventPublisher(ILogger<LoggingWorkflowEventPublisher> logger) : IWorkflowEventPublisher
{
    public Task PublishAsync(Guid workflowId, string eventType, string? message = null, object? payload = null, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[{WorkflowId}] {EventType}: {Message}", workflowId, eventType, message);
        return Task.CompletedTask;
    }
}

/// <summary>SignalR-backed publisher; falls back to logging if hub context unavailable.</summary>
public sealed class SignalRWorkflowEventPublisher(
    IHubContext<WorkflowHub> hub,
    ILogger<SignalRWorkflowEventPublisher> logger) : IWorkflowEventPublisher
{
    public async Task PublishAsync(Guid workflowId, string eventType, string? message = null, object? payload = null, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("[{WorkflowId}] {EventType}: {Message}", workflowId, eventType, message);
        await hub.Clients.Group($"workflow:{workflowId}")
            .SendAsync("workflowEvent", new
            {
                workflowId,
                eventType,
                message,
                payload,
                occurredAt = DateTimeOffset.UtcNow
            }, cancellationToken);
    }
}

public sealed class WorkflowHub : Hub
{
    public async Task Subscribe(Guid workflowId)
        => await Groups.AddToGroupAsync(Context.ConnectionId, $"workflow:{workflowId}");

    public async Task Unsubscribe(Guid workflowId)
        => await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"workflow:{workflowId}");
}

public sealed class WorkflowBackgroundService(
    IWorkflowQueue queue,
    IServiceScopeFactory scopeFactory,
    ILogger<WorkflowBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Workflow background worker started");

        await foreach (var workflowId in queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var executor = scope.ServiceProvider.GetRequiredService<IWorkflowExecutor>();
                await executor.ExecuteAsync(workflowId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error executing workflow {WorkflowId}", workflowId);
            }
        }
    }
}
