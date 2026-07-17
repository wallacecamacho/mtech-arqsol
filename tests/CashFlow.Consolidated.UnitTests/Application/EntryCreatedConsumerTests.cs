using CashFlow.Consolidated.Application.Consumers;
using CashFlow.Consolidated.Domain.Repositories;
using CashFlow.Consolidated.Infrastructure.Persistence;
using CashFlow.EventBus.Events;
using FluentAssertions;
using MassTransit;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CashFlow.Consolidated.UnitTests.Application;

/// <summary>
/// Tests for EntryCreatedConsumer: idempotency, credit/debit routing, unknown type, rollback on error.
/// Uses SQLite in-memory so transactions work correctly (EF Core InMemory does not support transactions).
/// </summary>
public class EntryCreatedConsumerTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection;
    private readonly ConsolidatedDbContext _dbContext;
    private readonly Mock<IDailyBalanceRepository> _repositoryMock;
    private readonly ProcessedEventRepository _processedEventRepo;
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly EntryCreatedConsumer _consumer;

    public EntryCreatedConsumerTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<ConsolidatedDbContext>()
            .UseSqlite(_connection)
            .Options;
        _dbContext = new ConsolidatedDbContext(options);
        _dbContext.Database.EnsureCreated();

        _repositoryMock = new Mock<IDailyBalanceRepository>();
        _processedEventRepo = new ProcessedEventRepository(_dbContext);
        _cacheMock = new Mock<IDistributedCache>();

        _consumer = new EntryCreatedConsumer(
            _repositoryMock.Object,
            _processedEventRepo,
            _dbContext,
            _cacheMock.Object,
            NullLogger<EntryCreatedConsumer>.Instance);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _connection.DisposeAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static ConsumeContext<EntryCreatedIntegrationEvent> CreateContext(
        EntryCreatedIntegrationEvent evt)
    {
        var mock = new Mock<ConsumeContext<EntryCreatedIntegrationEvent>>();
        mock.SetupGet(x => x.Message).Returns(evt);
        mock.SetupGet(x => x.CancellationToken).Returns(CancellationToken.None);
        return mock.Object;
    }

    private static EntryCreatedIntegrationEvent MakeEvent(
        string entryType = "Credit",
        decimal amount   = 200m,
        Guid? entryId    = null,
        Guid? merchantId = null) => new()
    {
        EntryId    = entryId    ?? Guid.NewGuid(),
        MerchantId = merchantId ?? Guid.NewGuid(),
        Amount     = amount,
        Currency   = "BRL",
        EntryType  = entryType,
        EntryDate  = DateTime.UtcNow.Date,
        Description = "unit-test"
    };

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Consume_CreditEntry_ShouldCallApplyEntryWithCorrectAmounts()
    {
        var evt = MakeEvent("Credit", 200m);
        _repositoryMock.Setup(r => r.ApplyEntryAsync(
            evt.MerchantId, evt.EntryDate.Date, 200m, 0m, "BRL", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _consumer.Consume(CreateContext(evt));

        _repositoryMock.Verify(r => r.ApplyEntryAsync(
            evt.MerchantId, evt.EntryDate.Date, 200m, 0m, "BRL", It.IsAny<CancellationToken>()),
            Times.Once);

        var processed = await _dbContext.ProcessedEvents.FindAsync(evt.EntryId);
        processed.Should().NotBeNull("event should be marked as processed after successful consume");
    }

    [Fact]
    public async Task Consume_DebitEntry_ShouldCallApplyEntryWithCorrectAmounts()
    {
        var evt = MakeEvent("Debit", 75m);
        _repositoryMock.Setup(r => r.ApplyEntryAsync(
            evt.MerchantId, evt.EntryDate.Date, 0m, 75m, "BRL", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await _consumer.Consume(CreateContext(evt));

        _repositoryMock.Verify(r => r.ApplyEntryAsync(
            evt.MerchantId, evt.EntryDate.Date, 0m, 75m, "BRL", It.IsAny<CancellationToken>()),
            Times.Once);

        var processed = await _dbContext.ProcessedEvents.FindAsync(evt.EntryId);
        processed.Should().NotBeNull();
    }

    [Fact]
    public async Task Consume_DuplicateEvent_ShouldSkipApplyEntryAndNotThrow()
    {
        var entryId = Guid.NewGuid();

        // Pre-seed: mark the event as already processed
        await _dbContext.ProcessedEvents.AddAsync(
            new CashFlow.Consolidated.Domain.Entities.ProcessedEvent
            { EntryId = entryId, ProcessedAt = DateTime.UtcNow });
        await _dbContext.SaveChangesAsync();

        var evt = MakeEvent("Credit", 300m, entryId: entryId);

        // Act — should not throw and should NOT call ApplyEntryAsync
        await _consumer.Consume(CreateContext(evt));

        _repositoryMock.Verify(r => r.ApplyEntryAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Consume_UnknownEntryType_ShouldSkipApplyEntry()
    {
        var evt = MakeEvent("Void", 100m);

        await _consumer.Consume(CreateContext(evt));

        _repositoryMock.Verify(r => r.ApplyEntryAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Never);

        // ProcessedEvent should NOT be saved (we returned early without a transaction)
        var processed = await _dbContext.ProcessedEvents.FindAsync(evt.EntryId);
        processed.Should().BeNull();
    }

    [Fact]
    public async Task Consume_WhenApplyEntryFails_ShouldRollbackAndNotMarkProcessed()
    {
        var evt = MakeEvent("Credit", 500m);

        _repositoryMock.Setup(r => r.ApplyEntryAsync(
            It.IsAny<Guid>(), It.IsAny<DateTime>(),
            It.IsAny<decimal>(), It.IsAny<decimal>(), It.IsAny<string>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error simulation"));

        // Consumer re-throws — MassTransit will retry
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _consumer.Consume(CreateContext(evt)));

        // Transaction was rolled back — ProcessedEvent must NOT exist
        var processed = await _dbContext.ProcessedEvents.FindAsync(evt.EntryId);
        processed.Should().BeNull("transaction rollback must prevent ProcessedEvent from being saved");
    }
}
