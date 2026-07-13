using CashFlow.SharedKernel.Domain;
using FluentAssertions;
using Xunit;

namespace CashFlow.Entries.UnitTests.Domain;

public class MoneyTests
{
    [Fact]
    public void Create_WithValidAmount_ShouldSucceed()
    {
        var money = new Money(100.50m, "BRL");

        money.Amount.Should().Be(100.50m);
        money.Currency.Should().Be("BRL");
    }

    [Fact]
    public void Create_WithNegativeAmount_ShouldThrow()
    {
        var act = () => new Money(-1m, "BRL");
        act.Should().Throw<ArgumentException>().WithMessage("*negative*");
    }

    [Fact]
    public void Add_SameCurrency_ShouldReturnSum()
    {
        var a = new Money(100m, "BRL");
        var b = new Money(50m, "BRL");

        var result = a.Add(b);

        result.Amount.Should().Be(150m);
        result.Currency.Should().Be("BRL");
    }

    [Fact]
    public void Add_DifferentCurrencies_ShouldThrow()
    {
        var brl = new Money(100m, "BRL");
        var usd = new Money(50m, "USD");

        var act = () => brl.Add(usd);

        act.Should().Throw<InvalidOperationException>().WithMessage("*currencies*");
    }

    [Fact]
    public void Equality_SameAmountAndCurrency_ShouldBeEqual()
    {
        var a = new Money(100m, "BRL");
        var b = new Money(100m, "BRL");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void Zero_ShouldReturnMoneyWithZeroAmount()
    {
        var zero = Money.Zero("BRL");

        zero.Amount.Should().Be(0m);
        zero.Currency.Should().Be("BRL");
    }
}
