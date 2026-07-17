using CashFlow.Consolidated.Domain.Repositories;
using CashFlow.SharedKernel.Application;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace CashFlow.Consolidated.Application.Queries;

public record GetDailyBalanceRangeQuery(
    Guid MerchantId,
    DateTime From,
    DateTime To
) : IRequest<Result<IEnumerable<DailyBalanceDto>>>;

public class GetDailyBalanceRangeQueryHandler
    : IRequestHandler<GetDailyBalanceRangeQuery, Result<IEnumerable<DailyBalanceDto>>>
{
    private readonly IDailyBalanceRepository _repository;

    public GetDailyBalanceRangeQueryHandler(IDailyBalanceRepository repository)
        => _repository = repository;

    public async Task<Result<IEnumerable<DailyBalanceDto>>> Handle(
        GetDailyBalanceRangeQuery request, CancellationToken cancellationToken)
    {
        var from = request.From.Date;
        var to   = request.To.Date;

        if (from > to)
            return Result.Failure<IEnumerable<DailyBalanceDto>>(
                "'from' must be earlier than or equal to 'to'.");

        if ((to - from).TotalDays > 365)
            return Result.Failure<IEnumerable<DailyBalanceDto>>(
                "Range cannot exceed 365 days.");

        var balances = await _repository.GetByMerchantAndDateRangeAsync(
            request.MerchantId, from, to, cancellationToken);

        var dtos = balances.Select(b => new DailyBalanceDto(
            b.MerchantId,
            b.Date,
            b.TotalCredits,
            b.TotalDebits,
            b.Balance,
            b.Currency));

        return Result.Success(dtos);
    }
}
