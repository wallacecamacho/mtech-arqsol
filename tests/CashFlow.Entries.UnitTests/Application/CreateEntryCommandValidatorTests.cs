using CashFlow.Entries.Application.Commands;
using FluentAssertions;
using FluentValidation.TestHelper;
using Xunit;

namespace CashFlow.Entries.UnitTests.Application;

public class CreateEntryCommandValidatorTests
{
    private readonly CreateEntryCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_ShouldPass()
    {
        var command = ValidCommand();
        var result = _validator.TestValidate(command);
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Validate_InvalidAmount_ShouldFail(decimal amount)
    {
        var command = ValidCommand() with { Amount = amount };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Amount);
    }

    [Fact]
    public void Validate_EmptyDescription_ShouldFail()
    {
        var command = ValidCommand() with { Description = "" };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.Description);
    }

    [Fact]
    public void Validate_EmptyMerchantId_ShouldFail()
    {
        var command = ValidCommand() with { MerchantId = Guid.Empty };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.MerchantId);
    }

    [Fact]
    public void Validate_FutureDate_ShouldFail()
    {
        var command = ValidCommand() with { EntryDate = DateTime.UtcNow.Date.AddDays(2) };
        var result = _validator.TestValidate(command);
        result.ShouldHaveValidationErrorFor(x => x.EntryDate);
    }

    private static CreateEntryCommand ValidCommand() => new(
        Amount: 100m,
        Currency: "BRL",
        Type: CashFlow.Entries.Domain.Entities.EntryType.Credit,
        Description: "Test entry",
        EntryDate: DateTime.UtcNow.Date,
        MerchantId: Guid.NewGuid());
}
