using CashFlow.Consolidated.Domain.Repositories;
using CashFlow.Consolidated.Infrastructure.Persistence;
using CashFlow.EventBus.Events;
using MassTransit;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace CashFlow.Consolidated.Application.Consumers;

public class EntryCreatedConsumer : IConsumer<EntryCreatedIntegrationEvent>
{
    private readonly IDailyBalanceRepository _repository;
    private readonly IProcessedEventRepository _processedEventRepository;
    private readonly ConsolidatedDbContext _dbContext;
    private readonly IDistributedCache _cache;
    private readonly ILogger<EntryCreatedConsumer> _logger;

    public EntryCreatedConsumer(
        IDailyBalanceRepository repository,
        IProcessedEventRepository processedEventRepository,
        ConsolidatedDbContext dbContext,
        IDistributedCache cache,
        ILogger<EntryCreatedConsumer> logger)
    {
        _repository               = repository;
        _processedEventRepository = processedEventRepository;
        _dbContext                = dbContext;
        _cache                    = cache;
        _logger                   = logger;
    }

    public async Task Consume(ConsumeContext<EntryCreatedIntegrationEvent> context)
    {
        var evt        = context.Message;
        var merchantId = evt.MerchantId;
        var date       = evt.EntryDate.Date;
        var ct         = context.CancellationToken;

        var credits = evt.EntryType.Equals("Credit", StringComparison.OrdinalIgnoreCase) ? evt.Amount : 0m;
        var debits  = evt.EntryType.Equals("Debit",  StringComparison.OrdinalIgnoreCase) ? evt.Amount : 0m;

        if (credits == 0m && debits == 0m)
        {
            _logger.LogWarning("Unknown entry type '{EntryType}' for event {EventId} — ignoring.",
                evt.EntryType, evt.Id);
            return;
        }

        // ── Atomic transaction: idempotency check + upsert + mark processed ──────
        await using var tx = await _dbContext.Database.BeginTransactionAsync(ct);
        try
        {
            // 1. Idempotency: skip duplicates from at-least-once delivery
            if (await _processedEventRepository.ExistsAsync(evt.EntryId, ct))
            {
                _logger.LogInformation(
                    "Entry {EntryId} already processed — skipping duplicate delivery.", evt.EntryId);
                await tx.RollbackAsync(ct);
                return;
            }

            // 2. Upsert balance atomically (no race condition possible)
            await _repository.ApplyEntryAsync(merchantId, date, credits, debits, evt.Currency, ct);

            // 3. Mark event as processed (committed in the same transaction)
            await _processedEventRepository.AddAsync(evt.EntryId, ct);
            await _dbContext.SaveChangesAsync(ct);

            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        _logger.LogInformation(
            "DailyBalance updated — Entry={EntryId} Date={Date} Credits={Credits} Debits={Debits}",
            evt.EntryId, date.ToString("yyyy-MM-dd"), credits, debits);

        // ── Cache invalidation (outside tx — best-effort) ─────────────────────────
        try
        {
            await _cache.RemoveAsync($"dailybalance:{merchantId}:{date:yyyy-MM-dd}", ct);
        }
        catch (Exception cacheEx)
        {
            _logger.LogWarning(cacheEx,
                "Cache invalidation failed for {MerchantId}/{Date}. Next read will hit DB.",
                merchantId, date);
        }
    }
}

