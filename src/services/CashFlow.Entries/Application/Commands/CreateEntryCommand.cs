using CashFlow.Entries.Domain.Entities;
using CashFlow.Entries.Domain.Repositories;
using CashFlow.EventBus.Abstractions;
using CashFlow.EventBus.Events;
using CashFlow.SharedKernel.Application;
using FluentValidation;
using MediatR;

namespace CashFlow.Entries.Application.Commands;

public record CreateEntryCommand(
    decimal Amount,
    string Currency,
    EntryType Type,
    string Description,
    DateTime EntryDate,
    Guid MerchantId
) : IRequest<Result<Guid>>;

public class CreateEntryCommandValidator : AbstractValidator<CreateEntryCommand>
{
    public CreateEntryCommandValidator()
    {
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Amount must be greater than zero.");
        RuleFor(x => x.Currency).NotEmpty().Length(3).WithMessage("Currency must be a 3-letter ISO code.");
        RuleFor(x => x.Type).IsInEnum().WithMessage("EntryType must be Credit or Debit.");
        RuleFor(x => x.Description).NotEmpty().MaximumLength(500);
        RuleFor(x => x.EntryDate).NotEmpty().LessThanOrEqualTo(DateTime.UtcNow.Date);
        RuleFor(x => x.MerchantId).NotEmpty();
    }
}

public class CreateEntryCommandHandler : IRequestHandler<CreateEntryCommand, Result<Guid>>
{
    private readonly IEntryRepository _repository;
    private readonly IEventBus _eventBus;

    public CreateEntryCommandHandler(IEntryRepository repository, IEventBus eventBus)
    {
        _repository = repository;
        _eventBus = eventBus;
    }

    public async Task<Result<Guid>> Handle(CreateEntryCommand request, CancellationToken cancellationToken)
    {
        var entry = Entry.Create(
            request.Amount,
            request.Currency,
            request.Type,
            request.Description,
            request.EntryDate,
            request.MerchantId);

        await _repository.AddAsync(entry, cancellationToken);
        await _repository.SaveChangesAsync(cancellationToken);

        var integrationEvent = new EntryCreatedIntegrationEvent
        {
            EntryId = entry.Id,
            MerchantId = entry.MerchantId,
            Amount = entry.Amount.Amount,
            Currency = entry.Amount.Currency,
            EntryType = entry.Type.ToString(),
            Description = entry.Description,
            EntryDate = entry.EntryDate
        };

        await _eventBus.PublishAsync(integrationEvent, cancellationToken);

        return Result.Success(entry.Id);
    }
}
