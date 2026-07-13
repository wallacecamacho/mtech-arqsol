using CashFlow.SharedKernel.Domain;

namespace CashFlow.Entries.Domain.Entities;

public record EntryCreatedDomainEvent(
    Guid EntryId,
    decimal Amount,
    string Currency,
    EntryType EntryType,
    string Description,
    DateTime EntryDate,
    Guid MerchantId
) : DomainEvent
{
    public override string EventType => "entry.created";
}
