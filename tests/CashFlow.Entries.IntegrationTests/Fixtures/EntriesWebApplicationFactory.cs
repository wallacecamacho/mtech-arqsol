using CashFlow.Entries.Infrastructure.Persistence;
using CashFlow.EventBus;
using CashFlow.EventBus.Abstractions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Xunit;
using Testcontainers.RabbitMq;

namespace CashFlow.Entries.IntegrationTests.Fixtures;

public class EntriesWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("cashflow_entries_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly RabbitMqContainer _rabbitmq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _rabbitmq.StartAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace real DB with test container DB
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<EntriesDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddDbContext<EntriesDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));

            // Build and migrate
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<EntriesDbContext>();
            db.Database.Migrate();
        });

        builder.UseSetting("RabbitMQ:Host", _rabbitmq.Hostname);
        builder.UseSetting("RabbitMQ:Port", _rabbitmq.GetMappedPublicPort(5672).ToString());
        builder.UseSetting("RabbitMQ:Username", "guest");
        builder.UseSetting("RabbitMQ:Password", "guest");
        builder.UseSetting("Jwt:Key", "INTEGRATION_TEST_KEY_ABCDEFGHIJKLMNOP");
        builder.UseSetting("Jwt:Issuer", "cashflow-gateway");
        builder.UseSetting("Jwt:Audience", "cashflow-services");
        builder.UseSetting("Otlp:Endpoint", "http://localhost:4317");

        // Replace IEventBus with no-op so tests don't depend on MassTransit bus state
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll(typeof(IEventBus));
            services.AddSingleton<IEventBus, NoOpEventBus>();
        });
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await _rabbitmq.DisposeAsync();
    }

    private sealed class NoOpEventBus : IEventBus
    {
        public Task PublishAsync<T>(T integrationEvent, CancellationToken cancellationToken = default)
            where T : IntegrationEvent => Task.CompletedTask;
    }
}
