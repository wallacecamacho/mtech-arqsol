using CashFlow.Entries.Domain.Outbox;
using CashFlow.Entries.Infrastructure.Persistence;
using CashFlow.EventBus.Abstractions;
using CashFlow.EventBus.Events;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace CashFlow.Entries.Infrastructure;

public class OutboxProcessorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxProcessorBackgroundService> _logger;
    private static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(5);
    private const int MaxRetries = 10;

    public OutboxProcessorBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<OutboxProcessorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox processor started.");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingMessagesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in outbox processor loop.");
            }

            try
            {
                await Task.Delay(PollingInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox processor stopped.");
    }

    private async Task ProcessPendingMessagesAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<EntriesDbContext>();
        var eventBus = scope.ServiceProvider.GetRequiredService<IEventBus>();

        var pending = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.RetryCount < MaxRetries)
            .OrderBy(m => m.OccurredAt)
            .Take(50)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            await DeadLetterExceededMessagesAsync(dbContext, ct);
            return;
        }

        _logger.LogInformation("Processing {Count} outbox message(s).", pending.Count);

        foreach (var message in pending)
        {
            try
            {
                await PublishMessageAsync(message, eventBus, ct);
                message.ProcessedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                message.RetryCount++;
                message.Error = ex.Message.Length <= 2000 ? ex.Message : ex.Message[..2000];
                _logger.LogError(ex, "Failed to publish outbox message {Id} (EventType={EventType}, Retry={Retry})",
                    message.Id, message.EventType, message.RetryCount);
            }
        }

        await dbContext.SaveChangesAsync(ct);

        // Always sweep dead-letter messages, not only when the active queue is empty.
        // Without this, a continuous stream of new messages would starve the dead-letter check.
        await DeadLetterExceededMessagesAsync(dbContext, ct);
    }

    private static async Task PublishMessageAsync(OutboxMessage message, IEventBus eventBus, CancellationToken ct)
    {
        switch (message.EventType)
        {
            case "entry.created":
                var evt = JsonSerializer.Deserialize<EntryCreatedIntegrationEvent>(message.Payload)
                          ?? throw new InvalidOperationException("Failed to deserialize EntryCreatedIntegrationEvent.");
                await eventBus.PublishAsync(evt, ct);
                break;
            default:
                throw new InvalidOperationException($"Unknown outbox event type: '{message.EventType}'.");
        }
    }

    /// <summary>Marks messages that exceeded MaxRetries as dead-lettered so they no longer block polling.</summary>
    private async Task DeadLetterExceededMessagesAsync(EntriesDbContext dbContext, CancellationToken ct)
    {
        var deadMessages = await dbContext.OutboxMessages
            .Where(m => m.ProcessedAt == null && m.RetryCount >= MaxRetries)
            .ToListAsync(ct);

        if (deadMessages.Count == 0) return;

        foreach (var msg in deadMessages)
        {
            msg.ProcessedAt = DateTime.UtcNow;
            msg.Error = $"DEAD_LETTER: exceeded {MaxRetries} retries. Last error: {msg.Error}";
            _logger.LogError(
                "Outbox message {Id} (EventType={EventType}) dead-lettered after {Retries} retries.",
                msg.Id, msg.EventType, msg.RetryCount);
        }

        await dbContext.SaveChangesAsync(ct);
    }
}
