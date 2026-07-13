using CashFlow.Consolidated.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Consolidated.Infrastructure.Persistence;

public class ConsolidatedDbContext : DbContext
{
    public DbSet<DailyBalance> DailyBalances => Set<DailyBalance>();

    public ConsolidatedDbContext(DbContextOptions<ConsolidatedDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DailyBalance>(entity =>
        {
            entity.ToTable("daily_balances");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.MerchantId).HasColumnName("merchant_id");
            entity.Property(e => e.Date).HasColumnName("date").HasColumnType("date");
            entity.Property(e => e.TotalCredits).HasColumnName("total_credits").HasPrecision(18, 4);
            entity.Property(e => e.TotalDebits).HasColumnName("total_debits").HasPrecision(18, 4);
            entity.Property(e => e.Currency).HasColumnName("currency").HasMaxLength(3);
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.HasIndex(e => new { e.MerchantId, e.Date })
                .IsUnique()
                .HasDatabaseName("idx_daily_balances_merchant_date_unique");
        });
    }
}
