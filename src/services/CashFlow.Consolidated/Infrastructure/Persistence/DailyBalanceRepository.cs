using CashFlow.Consolidated.Domain.Entities;
using CashFlow.Consolidated.Domain.Repositories;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidated.Infrastructure.Persistence;

public class DailyBalanceRepository : IDailyBalanceRepository
{
    private readonly ConsolidatedDbContext _context;

    public DailyBalanceRepository(ConsolidatedDbContext context)
    {
        _context = context;
    }

    public async Task<DailyBalance?> GetByMerchantAndDateAsync(Guid merchantId, DateTime date, CancellationToken cancellationToken = default)
        => await _context.DailyBalances
            .FirstOrDefaultAsync(b => b.MerchantId == merchantId && b.Date == date.Date, cancellationToken);

    public async Task<IEnumerable<DailyBalance>> GetByMerchantAndDateRangeAsync(Guid merchantId, DateTime from, DateTime to, CancellationToken cancellationToken = default)
        => await _context.DailyBalances
            .Where(b => b.MerchantId == merchantId && b.Date >= from.Date && b.Date <= to.Date)
            .OrderByDescending(b => b.Date)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(DailyBalance balance, CancellationToken cancellationToken = default)
        => await _context.DailyBalances.AddAsync(balance, cancellationToken);

    public void Update(DailyBalance balance)
        => _context.DailyBalances.Update(balance);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => await _context.SaveChangesAsync(cancellationToken);
}
