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

    public async Task ApplyEntryAsync(
        Guid merchantId, DateTime date, decimal credits, decimal debits,
        string currency, CancellationToken cancellationToken = default)
    {
        var id = Guid.NewGuid();
        var d  = date.Date;
        // Single atomic statement: insert new row or increment existing totals.
        // FormattableString creates parameterized SQL — no SQL injection risk.
        await _context.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO daily_balances
                (id, merchant_id, date, total_credits, total_debits, currency, created_at, updated_at)
            VALUES
                ({id}, {merchantId}, {d}, {credits}, {debits}, {currency}, NOW(), NOW())
            ON CONFLICT (merchant_id, date) DO UPDATE SET
                total_credits = daily_balances.total_credits + {credits},
                total_debits  = daily_balances.total_debits  + {debits},
                updated_at    = NOW()
            """, cancellationToken);
    }
}
