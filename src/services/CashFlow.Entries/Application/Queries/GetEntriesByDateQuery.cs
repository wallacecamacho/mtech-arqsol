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

public record PagedResult<T>(IEnumerable<T> Items, int TotalCount, int Page, int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(TotalCount / (double)PageSize);
}

public record GetEntriesByDateQuery(
    Guid MerchantId,
    DateTime Date,
    int Page     = 1,
    int PageSize = 50
) : IRequest<Result<PagedResult<EntryDto>>>;

public class GetEntriesByDateQueryHandler : IRequestHandler<GetEntriesByDateQuery, Result<PagedResult<EntryDto>>>
{
    private const int MaxPageSize = 100;
    private readonly IEntryRepository _repository;

    public GetEntriesByDateQueryHandler(IEntryRepository repository) => _repository = repository;

    public async Task<Result<PagedResult<EntryDto>>> Handle(GetEntriesByDateQuery request, CancellationToken cancellationToken)
    {
        var page     = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, MaxPageSize);

        var (entries, total) = await _repository.GetByDatePagedAsync(
            request.MerchantId, request.Date, page, pageSize, cancellationToken);

        var dtos = entries.Select(e => new EntryDto(
            e.Id,
            e.Amount.Amount,
            e.Amount.Currency,
            e.Type.ToString(),
            e.Description,
            e.EntryDate,
            e.CreatedAt));

        return Result.Success(new PagedResult<EntryDto>(dtos, total, page, pageSize));
    }
}
