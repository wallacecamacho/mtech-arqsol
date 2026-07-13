using CashFlow.Entries.Application.Commands;
using CashFlow.Entries.Domain.Entities;
using CashFlow.Entries.Domain.Repositories;
using CashFlow.EventBus.Abstractions;
using CashFlow.EventBus.Events;
using FluentAssertions;
using Moq;
using Xunit;

namespace CashFlow.Entries.UnitTests.Application;

public class CreateEntryCommandHandlerTests
{
    private readonly Mock<IEntryRepository> _repositoryMock;
    private readonly Mock<IEventBus> _eventBusMock;
    private readonly CreateEntryCommandHandler _handler;

    public CreateEntryCommandHandlerTests()
    {
        _repositoryMock = new Mock<IEntryRepository>();
        _eventBusMock = new Mock<IEventBus>();
        _handler = new CreateEntryCommandHandler(_repositoryMock.Object, _eventBusMock.Object);
    }

    [Fact]
    public async Task Handle_ValidCommand_ShouldCreateEntryAndPublishEvent()
    {
        var command = new CreateEntryCommand(
            Amount: 100m,
            Currency: "BRL",
            Type: EntryType.Credit,
            Description: "Venda de produto",
            EntryDate: DateTime.UtcNow.Date,
            MerchantId: Guid.NewGuid());

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        _eventBusMock.Setup(e => e.PublishAsync(It.IsAny<EntryCreatedIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBe(Guid.Empty);

        _repositoryMock.Verify(r => r.AddAsync(It.IsAny<Entry>(), It.IsAny<CancellationToken>()), Times.Once);
        _repositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        _eventBusMock.Verify(e => e.PublishAsync(It.IsAny<EntryCreatedIntegrationEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidDebitCommand_ShouldPublishEventWithDebitType()
    {
        var command = new CreateEntryCommand(
            Amount: 50m,
            Currency: "BRL",
            Type: EntryType.Debit,
            Description: "Compra de suprimentos",
            EntryDate: DateTime.UtcNow.Date,
            MerchantId: Guid.NewGuid());

        _repositoryMock.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        EntryCreatedIntegrationEvent? capturedEvent = null;
        _eventBusMock.Setup(e => e.PublishAsync(It.IsAny<EntryCreatedIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Callback<EntryCreatedIntegrationEvent, CancellationToken>((evt, _) => capturedEvent = evt)
            .Returns(Task.CompletedTask);

        await _handler.Handle(command, CancellationToken.None);

        capturedEvent.Should().NotBeNull();
        capturedEvent!.EntryType.Should().Be("Debit");
        capturedEvent.Amount.Should().Be(50m);
    }
}
