using CashFlow.Entries.Domain.Entities;

namespace CashFlow.Entries.Domain.Repositories;

public interface IEntryRepository
{
    Task<Entry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entry>> GetByDateAsync(Guid merchantId, DateTime date, CancellationToken cancellationToken = default);
    Task<IEnumerable<Entry>> GetByDateRangeAsync(Guid merchantId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
    Task AddAsync(Entry entry, CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
