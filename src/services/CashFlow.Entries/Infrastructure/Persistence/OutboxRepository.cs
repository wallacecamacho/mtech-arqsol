using CashFlow.Entries.Domain.Outbox;
using CashFlow.Entries.Domain.Repositories;

namespace CashFlow.Entries.Infrastructure.Persistence;

public class OutboxRepository : IOutboxRepository
{
    private readonly EntriesDbContext _context;

    public OutboxRepository(EntriesDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(OutboxMessage message, CancellationToken cancellationToken = default)
        => await _context.OutboxMessages.AddAsync(message, cancellationToken);
}
