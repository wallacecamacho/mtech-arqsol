using CashFlow.Consolidated.Domain.Entities;
using CashFlow.Consolidated.Domain.Repositories;
using CashFlow.EventBus.Events;
using MassTransit;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidated.Application.Consumers;

public class EntryCreatedConsumer : IConsumer<EntryCreatedIntegrationEvent>
{
    private readonly IDailyBalanceRepository _repository;
    private readonly IDistributedCache _cache;
    private readonly ILogger<EntryCreatedConsumer> _logger;

    public EntryCreatedConsumer(IDailyBalanceRepository repository, IDistributedCache cache, ILogger<EntryCreatedConsumer> logger)
    {
        _repository = repository;
        _cache = cache;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<EntryCreatedIntegrationEvent> context)
    {
        var evt = context.Message;

        _logger.LogInformation("Processing EntryCreated event {EventId} for entry {EntryId} on {Date}",
            evt.Id, evt.EntryId, evt.EntryDate.ToString("yyyy-MM-dd"));

        var merchantId = evt.MerchantId;
        var date = evt.EntryDate.Date;

        var balance = await _repository.GetByMerchantAndDateAsync(merchantId, date, context.CancellationToken);

        if (balance is null)
        {
            balance = DailyBalance.Create(merchantId, date, evt.Currency);
            await _repository.AddAsync(balance, context.CancellationToken);
        }

        if (evt.EntryType.Equals("Credit", StringComparison.OrdinalIgnoreCase))
            balance.ApplyCredit(evt.Amount);
        else if (evt.EntryType.Equals("Debit", StringComparison.OrdinalIgnoreCase))
            balance.ApplyDebit(evt.Amount);
        else
        {
            _logger.LogWarning("Unknown entry type {EntryType} for event {EventId}", evt.EntryType, evt.Id);
            return;
        }

        _repository.Update(balance);
        await _repository.SaveChangesAsync(context.CancellationToken);

        // Invalidate cache so next read reflects the updated balance
        var cacheKey = $"dailybalance:{merchantId}:{date:yyyy-MM-dd}";
        await _cache.RemoveAsync(cacheKey, context.CancellationToken);

        _logger.LogInformation("DailyBalance updated for {Date}: Credits={Credits}, Debits={Debits}, Balance={Balance}",
            date.ToString("yyyy-MM-dd"), balance.TotalCredits, balance.TotalDebits, balance.Balance);
    }
}
