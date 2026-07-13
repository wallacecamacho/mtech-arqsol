using CashFlow.SharedKernel.Domain;

namespace CashFlow.Consolidated.Domain.Entities;

public class DailyBalance : Entity
{
    public DateTime Date { get; private set; }
    public Guid MerchantId { get; private set; }
    public decimal TotalCredits { get; private set; }
    public decimal TotalDebits { get; private set; }
    public decimal Balance => TotalCredits - TotalDebits;
    public string Currency { get; private set; } = "BRL";

    private DailyBalance() { }

    public static DailyBalance Create(Guid merchantId, DateTime date, string currency = "BRL")
    {
        return new DailyBalance
        {
            MerchantId = merchantId,
            Date = date.Date,
            Currency = currency,
            TotalCredits = 0,
            TotalDebits = 0
        };
    }

    public void ApplyCredit(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Credit amount must be positive.", nameof(amount));
        TotalCredits += amount;
        SetUpdatedAt();
    }

    public void ApplyDebit(decimal amount)
    {
        if (amount <= 0) throw new ArgumentException("Debit amount must be positive.", nameof(amount));
        TotalDebits += amount;
        SetUpdatedAt();
    }
}
