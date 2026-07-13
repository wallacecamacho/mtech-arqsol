namespace CashFlow.EventBus.Events;

public record EntryCreatedIntegrationEvent : IntegrationEvent
{
    public Guid EntryId { get; init; }
    public Guid MerchantId { get; init; }
    public decimal Amount { get; init; }
    public string Currency { get; init; } = "BRL";
    public string EntryType { get; init; } = string.Empty; // "Credit" or "Debit"
    public string Description { get; init; } = string.Empty;
    public DateTime EntryDate { get; init; }
    public override string EventType => "entry.created";
}
