using CashFlow.Entries.Domain.Outbox;

namespace CashFlow.Entries.Domain.Repositories;

public interface IOutboxRepository
{
    Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
