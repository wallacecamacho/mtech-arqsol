namespace CashFlow.EventBus.Abstractions;

public interface IEventHandler<in T> where T : IntegrationEvent
{
    Task HandleAsync(T integrationEvent, CancellationToken cancellationToken = default);
}
