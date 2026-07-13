namespace CashFlow.EventBus.Abstractions;

public interface IEventBus
{
    Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
        where T : IntegrationEvent;
}
