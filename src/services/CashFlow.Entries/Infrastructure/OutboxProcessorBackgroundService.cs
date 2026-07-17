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
            .Where(m => m.ProcessedAt == null)
            .OrderBy(m => m.OccurredAt)
            .Take(50)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

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
}
