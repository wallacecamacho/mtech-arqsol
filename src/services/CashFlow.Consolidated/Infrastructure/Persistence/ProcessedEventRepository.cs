using CashFlow.Consolidated.Domain.Entities;
using CashFlow.Consolidated.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidated.Infrastructure.Persistence;

public class ProcessedEventRepository : IProcessedEventRepository
{
    private readonly ConsolidatedDbContext _context;

    public ProcessedEventRepository(ConsolidatedDbContext context)
        => _context = context;

    public async Task<bool> ExistsAsync(Guid entryId, CancellationToken cancellationToken = default)
        => await _context.ProcessedEvents.AnyAsync(e => e.EntryId == entryId, cancellationToken);

    public async Task AddAsync(Guid entryId, CancellationToken cancellationToken = default)
        => await _context.ProcessedEvents.AddAsync(
            new ProcessedEvent { EntryId = entryId }, cancellationToken);
}
