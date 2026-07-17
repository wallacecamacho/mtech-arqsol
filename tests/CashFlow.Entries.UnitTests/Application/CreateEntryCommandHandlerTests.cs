using CashFlow.Entries.Application.Commands;
using CashFlow.Entries.Domain.Entities;
using CashFlow.Entries.Domain.Outbox;
using CashFlow.Entries.Domain.Repositories;
using FluentAssertions;
using Moq;
using Xunit;

namespace CashFlow.Entries.UnitTests.Application;

public class CreateEntryCommandHandlerTests
{
    private readonly Mock<IEntryRepository> _repositoryMock;
    private readonly Mock<IOutboxRepository> _outboxRepositoryMock;
    private readonly CreateEntryCommandHandler _handler;

    public CreateEntryCommandHandlerTests()
    {
        _repositoryMock = new Mock<IEntryRepository>();
        _outboxRepositoryMock = new Mock<IOutboxRepository>();
        _handler = new CreateEntryCommandHandler(_repositoryMock.Object, _outboxRepositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateEntryAndEnqueueOutboxMessage()
    {
        var command = new CreateEntryCommand(
            Amount: 100m,
            Currency: "BRL",
            Type: EntryType.Credit,
            Description: "Venda de produto",
            EntryDate: DateTime.UtcNow.Date,
            MerchantId: Guid.NewGuid());

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _outboxRepositoryMock.Setup(o => o.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);

        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<Entry>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _outboxRepositoryMock.Verify(o => o.AddAsync(
            It.Is<OutboxMessage>(m => m.EventType == "entry.created"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidDebitCommand_ShouldEnqueueOutboxMessageWithDebitType()
    {
        var command = new CreateEntryCommand(
            Amount: 50m,
            Currency: "BRL",
            Type: EntryType.Debit,
            Description: "Compra de suprimentos",
            EntryDate: DateTime.UtcNow.Date,
            MerchantId: Guid.NewGuid());

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        OutboxMessage? capturedMessage = null;
        _outboxRepositoryMock
            .Setup(o => o.AddAsync(It.IsAny<OutboxMessage>(), It.IsAny<CancellationToken>()))
            .Callback<OutboxMessage, CancellationToken>((msg, _) => capturedMessage = msg)
            .Returns(Task.CompletedTask);

        await _handler.Handle(command, CancellationToken.None);

        capturedMessage.Should().NotBeNull();
        capturedMessage!.EventType.Should().Be("entry.created");
        capturedMessage.Payload.Should().Contain("\"EntryType\":\"Debit\"");
        capturedMessage.Payload.Should().Contain("\"Amount\":50");
    }
}

