using CashFlow.Entries.Domain.Entities;
using CashFlow.Entries.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Entries.Infrastructure.Persistence;

public class EntryRepository : IEntryRepository
{
    private readonly EntriesDbContext _context;

    public EntryRepository(EntriesDbContext context)
    {
        _context = context;
    }

    public async Task<Entry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => await _context.Entries.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public async Task<IEnumerable<Entry>> GetByDateAsync(Guid merchantId, DateTime date, CancellationToken cancellationToken = default)
        => await _context.Entries
            .Where(e => e.MerchantId == merchantId && e.EntryDate == date.Date)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IEnumerable<Entry>> GetByDateRangeAsync(Guid merchantId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => await _context.Entries
            .Where(e => e.MerchantId == merchantId && e.EntryDate >= from.Date && e.EntryDate <= to.Date)
            .OrderByDescending(e => e.EntryDate)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Entry entry, CancellationToken cancellationToken = default)
        => await _context.Entries.AddAsync(entry, cancellationToken);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);
}
