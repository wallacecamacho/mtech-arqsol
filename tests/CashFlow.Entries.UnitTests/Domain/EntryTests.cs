using CashFlow.Entries.Domain.Entities;
using CashFlow.SharedKernel.Domain;
using FluentAssertions;
using Xunit;

namespace CashFlow.Entries.UnitTests.Domain;

public class EntryTests
{
    [Fact]
    public void Create_WithValidCreditData_ShouldCreateEntry()
    {
        var merchantId = Guid.NewGuid();
        var entry = Entry.Create(150.00m, "BRL", EntryType.Credit, "Venda de produto", DateTime.UtcNow.Date, merchantId);

        entry.Should().NotBeNull();
        entry.Amount.Amount.Should().Be(150.00m);
        entry.Amount.Currency.Should().Be("BRL");
        entry.Type.Should().Be(EntryType.Credit);
        entry.Description.Should().Be("Venda de produto");
        entry.MerchantId.Should().Be(merchantId);
        entry.Id.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void Create_WithValidDebitData_ShouldCreateEntry()
    {
        var merchantId = Guid.NewGuid();
        var entry = Entry.Create(50.00m, "BRL", EntryType.Debit, "Compra de suprimentos", DateTime.UtcNow.Date, merchantId);

        entry.Type.Should().Be(EntryType.Debit);
        entry.Amount.Amount.Should().Be(50.00m);
    }

    [Fact]
    public void Create_ShouldRaiseDomainEvent()
    {
        var merchantId = Guid.NewGuid();
        var entry = Entry.Create(100m, "BRL", EntryType.Credit, "Test", DateTime.UtcNow.Date, merchantId);

        entry.DomainEvents.Should().HaveCount(1);
        entry.DomainEvents.First().Should().BeOfType<EntryCreatedDomainEvent>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Create_WithNonPositiveAmount_ShouldThrow(decimal invalidAmount)
    {
        var act = () => Entry.Create(invalidAmount, "BRL", EntryType.Credit, "Test", DateTime.UtcNow.Date, Guid.NewGuid());

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_WithEmptyDescription_ShouldThrow()
    {
        var act = () => Entry.Create(100m, "BRL", EntryType.Credit, "", DateTime.UtcNow.Date, Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("*Description*");
    }

    [Fact]
    public void Create_WithFutureDate_ShouldThrow()
    {
        var futureDate = DateTime.UtcNow.Date.AddDays(2);
        var act = () => Entry.Create(100m, "BRL", EntryType.Credit, "Test", futureDate, Guid.NewGuid());

        act.Should().Throw<ArgumentException>().WithMessage("*future*");
    }

    [Fact]
    public void DomainEvent_ShouldContainCorrectData()
    {
        var merchantId = Guid.NewGuid();
        var entry = Entry.Create(200m, "BRL", EntryType.Debit, "Pagamento", DateTime.UtcNow.Date, merchantId);

        var domainEvent = entry.DomainEvents.OfType<EntryCreatedDomainEvent>().Single();

        domainEvent.EntryId.Should().Be(entry.Id);
        domainEvent.Amount.Should().Be(200m);
        domainEvent.Currency.Should().Be("BRL");
        domainEvent.EntryType.Should().Be(EntryType.Debit);
        domainEvent.MerchantId.Should().Be(merchantId);
    }
}
