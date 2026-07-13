using CashFlow.Entries.Domain.Entities;
using CashFlow.Entries.Domain.Repositories;
using CashFlow.SharedKernel.Application;
using MediatR;

namespace CashFlow.Entries.Application.Queries;

public record EntryDto(
    Guid Id,
    decimal Amount,
    string Currency,
    string Type,
    string Description,
    DateTime EntryDate,
    DateTime CreatedAt
);

public record GetEntriesByDateQuery(Guid MerchantId, DateTime Date) : IRequest<Result<IEnumerable<EntryDto>>>;

public class GetEntriesByDateQueryHandler : IRequestHandler<GetEntriesByDateQuery, Result<IEnumerable<EntryDto>>>
{
    private readonly IEntryRepository _repository;

    public GetEntriesByDateQueryHandler(IEntryRepository repository)
    {
        _repository = repository;
    }

    public async Task<Result<IEnumerable<EntryDto>>> Handle(GetEntriesByDateQuery request, CancellationToken cancellationToken)
    {
        var entries = await _repository.GetByDateAsync(request.MerchantId, request.Date, cancellationToken);

        var dtos = entries.Select(e => new EntryDto(
            e.Id,
            e.Amount.Amount,
            e.Amount.Currency,
            e.Type.ToString(),
            e.Description,
            e.EntryDate,
            e.CreatedAt));

        return Result.Success(dtos);
    }
}
