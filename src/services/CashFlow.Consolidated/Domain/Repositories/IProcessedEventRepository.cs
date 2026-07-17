namespace CashFlow.Consolidated.Domain.Repositories;

public interface IProcessedEventRepository
{
    Task<bool> ExistsAsync(Guid entryId, CancellationToken cancellationToken = default);
    Task AddAsync(Guid entryId, CancellationToken cancellationToken = default);
}
