using CashFlow.Consolidated.Domain.Entities;

namespace CashFlow.Consolidated.Domain.Repositories;

public interface IDailyBalanceRepository
{
    Task<DailyBalance?> GetByMerchantAndDateAsync(Guid merchantId, DateTime date, CancellationToken cancellationToken = default);
    Task<IEnumerable<DailyBalance>> GetByMerchantAndDateRangeAsync(Guid merchantId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task AddAsync(DailyBalance balance, CancellationToken cancellationToken = default);
    void Update(DailyBalance balance);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically inserts or updates the daily balance for the given merchant and date.
    /// Uses PostgreSQL ON CONFLICT to avoid race conditions under concurrent consumers.
    /// </summary>
    Task ApplyEntryAsync(Guid merchantId, DateTime date, decimal credits, decimal debits,
        string currency, CancellationToken cancellationToken = default);
}
