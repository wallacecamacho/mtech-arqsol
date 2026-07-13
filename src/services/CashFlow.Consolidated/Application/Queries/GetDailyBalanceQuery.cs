using CashFlow.Consolidated.Domain.Repositories;
using CashFlow.SharedKernel.Application;
using MediatR;
using Microsoft.Extensions.Caching.Distributed;
using System.Text.Json;

namespace CashFlow.Consolidated.Application.Queries;

public record DailyBalanceDto(
    Guid MerchantId,
    DateTime Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal Balance,
    string Currency
);

public record GetDailyBalanceQuery(Guid MerchantId, DateTime Date) : IRequest<Result<DailyBalanceDto?>>;

public class GetDailyBalanceQueryHandler : IRequestHandler<GetDailyBalanceQuery, Result<DailyBalanceDto?>>
{
    private readonly IDailyBalanceRepository _repository;
    private readonly IDistributedCache _cache;

    public GetDailyBalanceQueryHandler(IDailyBalanceRepository repository, IDistributedCache cache)
    {
        _repository = repository;
        _cache = cache;
    }

    public async Task<Result<DailyBalanceDto?>> Handle(GetDailyBalanceQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"dailybalance:{request.MerchantId}:{request.Date:yyyy-MM-dd}";
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);

        if (cached is not null)
        {
            var cachedDto = JsonSerializer.Deserialize<DailyBalanceDto>(cached);
            return Result.Success(cachedDto);
        }

        var balance = await _repository.GetByMerchantAndDateAsync(request.MerchantId, request.Date, cancellationToken);

        if (balance is null)
            return Result.Success<DailyBalanceDto?>(null);

        var dto = new DailyBalanceDto(
            balance.MerchantId,
            balance.Date,
            balance.TotalCredits,
            balance.TotalDebits,
            balance.Balance,
            balance.Currency);

        // Cache for 5 minutes (historical dates can be cached longer)
        var cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = request.Date.Date < DateTime.UtcNow.Date
                ? TimeSpan.FromHours(24)
                : TimeSpan.FromMinutes(5)
        };

        await _cache.SetStringAsync(cacheKey, JsonSerializer.Serialize(dto), cacheOptions, cancellationToken);

        return Result.Success<DailyBalanceDto?>(dto);
    }
}
