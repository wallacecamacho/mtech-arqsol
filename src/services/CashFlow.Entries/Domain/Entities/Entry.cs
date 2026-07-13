using CashFlow.SharedKernel.Domain;

namespace CashFlow.Entries.Domain.Entities;

public enum EntryType
{
    Credit = 1,
    Debit = 2
}

public class Entry : Entity
{
    public Money Amount { get; private set; } = null!;
    public EntryType Type { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public DateTime EntryDate { get; private set; }
    public Guid MerchantId { get; private set; }

    private Entry() { }

    public static Entry Create(decimal amount, string currency, EntryType type, string description, DateTime entryDate, Guid merchantId)
    {
        if (amount <= 0)
            throw new ArgumentException("Amount must be positive.", nameof(amount));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Description cannot be empty.", nameof(description));
        if (entryDate > DateTime.UtcNow.Date.AddDays(1))
            throw new ArgumentException("Entry date cannot be in the future.", nameof(entryDate));

        var entry = new Entry
        {
            Amount = new Money(amount, currency),
            Type = type,
            Description = description,
            EntryDate = entryDate.Date,
            MerchantId = merchantId
        };

        entry.AddDomainEvent(new EntryCreatedDomainEvent(entry.Id, amount, currency, type, description, entry.EntryDate, merchantId));
        return entry;
    }
}
