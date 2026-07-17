using CashFlow.Entries.Domain.Entities;
using CashFlow.Entries.Domain.Outbox;
using Microsoft.EntityFrameworkCore;

namespace CashFlow.Entries.Infrastructure.Persistence;

public class EntriesDbContext : DbContext
{
    public DbSet<Entry> Entries => Set<Entry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public EntriesDbContext(DbContextOptions<EntriesDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Entry>(entity =>
        {
            entity.ToTable("entries");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Type).HasColumnName("type").HasConversion<string>();
            entity.Property(e => e.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
            entity.Property(e => e.EntryDate).HasColumnName("entry_date").HasColumnType("date");
            entity.Property(e => e.MerchantId).HasColumnName("merchant_id");
            entity.Property(e => e.CreatedAt).HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasColumnName("updated_at");

            entity.OwnsOne(e => e.Amount, money =>
            {
                money.Property(m => m.Amount).HasColumnName("amount").HasPrecision(18, 4).IsRequired();
                money.Property(m => m.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
            });

            entity.HasIndex(e => new { e.MerchantId, e.EntryDate }).HasDatabaseName("idx_entries_merchant_date");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.ToTable("outbox_messages");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(200).IsRequired();
            entity.Property(e => e.Payload).HasColumnName("payload").IsRequired();
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            entity.Property(e => e.ProcessedAt).HasColumnName("processed_at");
            entity.Property(e => e.Error).HasColumnName("error").HasMaxLength(2000);
            entity.Property(e => e.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
            entity.HasIndex(e => e.ProcessedAt).HasDatabaseName("idx_outbox_processed_at");
        });
    }
}
