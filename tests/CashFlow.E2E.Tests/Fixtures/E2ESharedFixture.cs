// Assembly aliases — needed because both services define 'Program' in the global namespace.
extern alias EntriesApp;
extern alias ConsolidatedApp;

using EntriesApp::CashFlow.Entries.Infrastructure.Persistence;
using ConsolidatedApp::CashFlow.Consolidated.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Testcontainers.Redis;
using Xunit;

namespace CashFlow.E2E.Tests.Fixtures;

/// <summary>
/// Shared fixture that boots all containers once per test class run.
/// Both services share the same RabbitMQ instance so events actually flow
/// from Entries (via OutboxProcessor) to the Consolidated consumer.
/// </summary>
public sealed class E2ESharedFixture : IAsyncLifetime
{
    // ── Containers ───────────────────────────────────────────────────────────
    private readonly PostgreSqlContainer _entriesPostgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("e2e_entries").WithUsername("test").WithPassword("test")
        .Build();

    private readonly PostgreSqlContainer _consolidatedPostgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("e2e_consolidated").WithUsername("test").WithPassword("test")
        .Build();

    private readonly RabbitMqContainer _rabbitmq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management-alpine")
        .Build();

    private readonly RedisContainer _redis = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    // ── Public clients ───────────────────────────────────────────────────────
    public HttpClient EntriesClient { get; private set; } = null!;
    public HttpClient ConsolidatedClient { get; private set; } = null!;

    private WebApplicationFactory<EntriesApp::Program>? _entriesFactory;
    private WebApplicationFactory<ConsolidatedApp::Program>? _consolidatedFactory;

    // ── JWT settings shared with both test factories ──────────────────────────
    internal const string JwtKey    = "E2E_TEST_SECRET_KEY_ABCDEFGHIJKLMN";
    internal const string JwtIssuer = "cashflow-gateway";
    internal const string JwtAudience = "cashflow-services";

    public async Task InitializeAsync()
    {
        // Start all containers in parallel
        await Task.WhenAll(
            _entriesPostgres.StartAsync(),
            _consolidatedPostgres.StartAsync(),
            _rabbitmq.StartAsync(),
            _redis.StartAsync());

        _entriesFactory = BuildEntriesFactory();
        _consolidatedFactory = BuildConsolidatedFactory();

        EntriesClient     = _entriesFactory.CreateClient();
        ConsolidatedClient = _consolidatedFactory.CreateClient();

        // Allow MassTransit consumers and OutboxProcessor to connect and warm up
        await Task.Delay(TimeSpan.FromSeconds(4));
    }

    public async Task DisposeAsync()
    {
        EntriesClient.Dispose();
        ConsolidatedClient.Dispose();

        if (_entriesFactory is not null)     await _entriesFactory.DisposeAsync();
        if (_consolidatedFactory is not null) await _consolidatedFactory.DisposeAsync();

        await Task.WhenAll(
            _entriesPostgres.DisposeAsync().AsTask(),
            _consolidatedPostgres.DisposeAsync().AsTask(),
            _rabbitmq.DisposeAsync().AsTask(),
            _redis.DisposeAsync().AsTask());
    }

    // ── Factory builders ─────────────────────────────────────────────────────

    private WebApplicationFactory<EntriesApp::Program> BuildEntriesFactory() =>
        new WebApplicationFactory<EntriesApp::Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                // Replace DB with test container
                services.RemoveAll(typeof(DbContextOptions<EntriesDbContext>));
                services.AddDbContext<EntriesDbContext>(o =>
                    o.UseNpgsql(_entriesPostgres.GetConnectionString()));

                // Migrate
                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                scope.ServiceProvider.GetRequiredService<EntriesDbContext>().Database.Migrate();
            });

            // NOTE: IEventBus is NOT replaced → OutboxProcessor uses real MassTransit
            builder.UseSetting("RabbitMQ:Host",     _rabbitmq.Hostname);
            builder.UseSetting("RabbitMQ:Port",     _rabbitmq.GetMappedPublicPort(5672).ToString());
            builder.UseSetting("RabbitMQ:Username", "guest");
            builder.UseSetting("RabbitMQ:Password", "guest");
            builder.UseSetting("Jwt:Key",           JwtKey);
            builder.UseSetting("Jwt:Issuer",        JwtIssuer);
            builder.UseSetting("Jwt:Audience",      JwtAudience);
            builder.UseSetting("Otlp:Endpoint",     "http://localhost:4317");
            builder.UseSetting("Seq:ServerUrl",     "http://localhost:5341");
        });

    private WebApplicationFactory<ConsolidatedApp::Program> BuildConsolidatedFactory() =>
        new WebApplicationFactory<ConsolidatedApp::Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll(typeof(DbContextOptions<ConsolidatedDbContext>));
                services.AddDbContext<ConsolidatedDbContext>(o =>
                    o.UseNpgsql(_consolidatedPostgres.GetConnectionString()));

                using var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                scope.ServiceProvider.GetRequiredService<ConsolidatedDbContext>().Database.Migrate();
            });

            // Same RabbitMQ → consumer receives events published by Entries
            builder.UseSetting("ConnectionStrings:Redis",   _redis.GetConnectionString());
            builder.UseSetting("RabbitMQ:Host",             _rabbitmq.Hostname);
            builder.UseSetting("RabbitMQ:Port",             _rabbitmq.GetMappedPublicPort(5672).ToString());
            builder.UseSetting("RabbitMQ:Username",         "guest");
            builder.UseSetting("RabbitMQ:Password",         "guest");
            builder.UseSetting("Jwt:Key",                   JwtKey);
            builder.UseSetting("Jwt:Issuer",                JwtIssuer);
            builder.UseSetting("Jwt:Audience",              JwtAudience);
            builder.UseSetting("Otlp:Endpoint",             "http://localhost:4317");
            builder.UseSetting("Seq:ServerUrl",             "http://localhost:5341");
        });
}
