using CashFlow.Consolidated.Domain.Entities;

namespace CashFlow.Consolidated.Domain.Repositories;

public interface IDailyBalanceRepository
{
    Task<DailyBalance?> GetByMerchantAndDateAsync(Guid merchantId, DateTime date, CancellationToken cancellationToken = default);
    Task<IEnumerable<DailyBalance>> GetByMerchantAndDateRangeAsync(Guid merchantId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task AddAsync(DailyBalance balance, CancellationToken cancellationToken = default);
    void Update(DailyBalance balance);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
