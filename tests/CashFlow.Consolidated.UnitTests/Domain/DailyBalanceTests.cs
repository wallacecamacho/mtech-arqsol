using CashFlow.Consolidated.Domain.Entities;
using FluentAssertions;
using Xunit;

namespace CashFlow.Consolidated.UnitTests.Domain;

public class DailyBalanceTests
{
    [Fact]
    public void Create_WithValidData_ShouldInitializeWithZeroBalances()
    {
        var merchantId = Guid.NewGuid();
        var date = DateTime.UtcNow.Date;

        var balance = DailyBalance.Create(merchantId, date, "BRL");

        balance.MerchantId.Should().Be(merchantId);
        balance.Date.Should().Be(date);
        balance.TotalCredits.Should().Be(0);
        balance.TotalDebits.Should().Be(0);
        balance.Balance.Should().Be(0);
        balance.Currency.Should().Be("BRL");
    }

    [Fact]
    public void ApplyCredit_ShouldIncreaseCredits()
    {
        var balance = DailyBalance.Create(Guid.NewGuid(), DateTime.UtcNow.Date);
        balance.ApplyCredit(150m);

        balance.TotalCredits.Should().Be(150m);
        balance.Balance.Should().Be(150m);
    }

    [Fact]
    public void ApplyDebit_ShouldIncreaseDebits()
    {
        var balance = DailyBalance.Create(Guid.NewGuid(), DateTime.UtcNow.Date);
        balance.ApplyDebit(50m);

        balance.TotalDebits.Should().Be(50m);
        balance.Balance.Should().Be(-50m);
    }

    [Fact]
    public void Balance_AfterMultipleOperations_ShouldBeCorrect()
    {
        var balance = DailyBalance.Create(Guid.NewGuid(), DateTime.UtcNow.Date);

        balance.ApplyCredit(500m);
        balance.ApplyCredit(200m);
        balance.ApplyDebit(150m);
        balance.ApplyDebit(75m);

        balance.TotalCredits.Should().Be(700m);
        balance.TotalDebits.Should().Be(225m);
        balance.Balance.Should().Be(475m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void ApplyCredit_WithNonPositiveAmount_ShouldThrow(decimal amount)
    {
        var balance = DailyBalance.Create(Guid.NewGuid(), DateTime.UtcNow.Date);
        var act = () => balance.ApplyCredit(amount);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-50)]
    public void ApplyDebit_WithNonPositiveAmount_ShouldThrow(decimal amount)
    {
        var balance = DailyBalance.Create(Guid.NewGuid(), DateTime.UtcNow.Date);
        var act = () => balance.ApplyDebit(amount);
        act.Should().Throw<ArgumentException>();
    }
}
