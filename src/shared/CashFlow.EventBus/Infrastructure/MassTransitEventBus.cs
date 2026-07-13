using CashFlow.EventBus.Abstractions;
using MassTransit;

namespace CashFlow.EventBus.Infrastructure;

public class MassTransitEventBus : IEventBus
{
    private readonly IPublishEndpoint _publishEndpoint;

    public MassTransitEventBus(IPublishEndpoint publishEndpoint)
    {
        _publishEndpoint = publishEndpoint;
    }

    public async Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IntegrationEvent
    {
        await _publishEndpoint.Publish(integrationEvent, cancellationToken);
    }
}
