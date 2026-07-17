namespace CashFlow.Consolidated.Domain.Entities;

/// <summary>
/// Records every EntryId that has already been applied to a DailyBalance.
/// Used by EntryCreatedConsumer to guarantee idempotency under at-least-once delivery.
/// </summary>
public class ProcessedEvent
{
    public Guid EntryId { get; init; }
    public DateTime ProcessedAt { get; init; } = DateTime.UtcNow;
}
