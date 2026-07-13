using CashFlow.Consolidated.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;

namespace CashFlow.Consolidated.IntegrationTests.Fixtures;

public class ConsolidatedWebApplicationFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("cashflow_consolidated_test")
        .WithUsername("test")
        .WithPassword("test")
        .Build();

    private readonly RabbitMqContainer _rabbitmq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitmq.StartAsync(), _redis.StartAsync());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ConsolidatedDbContext>));
            if (descriptor is not null) services.Remove(descriptor);

            services.AddDbContext<ConsolidatedDbContext>(options =>
                options.UseNpgsql(_postgres.GetConnectionString()));

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ConsolidatedDbContext>();
            db.Database.Migrate();
        });

        builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
        builder.UseSetting("ConnectionStrings:Redis", _redis.GetConnectionString());
        builder.UseSetting("RabbitMQ:Host", _rabbitmq.Hostname);
        builder.UseSetting("RabbitMQ:Port", _rabbitmq.GetMappedPublicPort(5672).ToString());
        builder.UseSetting("RabbitMQ:Username", "guest");
        builder.UseSetting("RabbitMQ:Password", "guest");
        builder.UseSetting("Jwt:Key", "INTEGRATION_TEST_KEY_ABCDEFGHIJKLMNOP");
        builder.UseSetting("Jwt:Issuer", "cashflow-gateway");
        builder.UseSetting("Jwt:Audience", "cashflow-services");
        builder.UseSetting("Otlp:Endpoint", "http://localhost:4317");
    }

    public new async Task DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbitmq.DisposeAsync().AsTask(), _redis.DisposeAsync().AsTask());
    }
}
