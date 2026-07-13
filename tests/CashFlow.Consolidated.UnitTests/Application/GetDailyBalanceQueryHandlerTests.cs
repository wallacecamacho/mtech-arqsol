using CashFlow.Consolidated.Application.Queries;
using CashFlow.Consolidated.Domain.Entities;
using CashFlow.Consolidated.Domain.Repositories;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Moq;
using Xunit;

namespace CashFlow.Consolidated.UnitTests.Application;

public class GetDailyBalanceQueryHandlerTests
{
    private readonly Mock<IDailyBalanceRepository> _repositoryMock;
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly GetDailyBalanceQueryHandler _handler;

    public GetDailyBalanceQueryHandlerTests()
    {
        _repositoryMock = new Mock<IDailyBalanceRepository>();
        _cacheMock = new Mock<IDistributedCache>();
        _handler = new GetDailyBalanceQueryHandler(_repositoryMock.Object, _cacheMock.Object);
    }

    [Fact]
    public async Task Handle_WhenBalanceExists_ShouldReturnDto()
    {
        var merchantId = Guid.NewGuid();
        var date = DateTime.UtcNow.Date;
        var balance = DailyBalance.Create(merchantId, date);
        balance.ApplyCredit(500m);
        balance.ApplyDebit(200m);

        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _repositoryMock.Setup(r => r.GetByMerchantAndDateAsync(merchantId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync(balance);
        _cacheMock.Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<DistributedCacheEntryOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await _handler.Handle(new GetDailyBalanceQuery(merchantId, date), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
        result.Value!.TotalCredits.Should().Be(500m);
        result.Value.TotalDebits.Should().Be(200m);
        result.Value.Balance.Should().Be(300m);
    }

    [Fact]
    public async Task Handle_WhenNoBalanceFound_ShouldReturnNull()
    {
        var merchantId = Guid.NewGuid();
        var date = DateTime.UtcNow.Date;

        _cacheMock.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);
        _repositoryMock.Setup(r => r.GetByMerchantAndDateAsync(merchantId, date, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DailyBalance?)null);

        var result = await _handler.Handle(new GetDailyBalanceQuery(merchantId, date), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }
}
