// Assembly aliases — see CashFlow.E2E.Tests.csproj
extern alias EntriesApp;
extern alias ConsolidatedApp;

using CashFlow.E2E.Tests.Fixtures;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;

namespace CashFlow.E2E.Tests;

/// <summary>
/// End-to-end tests that exercise the full event-driven flow:
///   POST /api/entries  →  Outbox → RabbitMQ  →  EntryCreatedConsumer
///                      →  DailyBalance updated  →  GET /api/consolidated
///
/// Both services run as in-process WebApplicationFactory instances sharing
/// the same RabbitMQ Testcontainer, proving the async integration contract.
/// </summary>
[Collection("E2E")]
public class CrossServiceFlowTests : IClassFixture<E2ESharedFixture>
{
    private readonly HttpClient _entries;
    private readonly HttpClient _consolidated;
    private readonly Guid _merchantId = Guid.NewGuid();

    public CrossServiceFlowTests(E2ESharedFixture fixture)
    {
        _entries     = fixture.EntriesClient;
        _consolidated = fixture.ConsolidatedClient;

        var token = GenerateToken(_merchantId);
        _entries.DefaultRequestHeaders.Authorization     = new AuthenticationHeaderValue("Bearer", token);
        _consolidated.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>
    /// Core scenario: creates a credit entry and waits (up to 15 s) for the
    /// Consolidated service to reflect the event in the daily balance.
    /// Proves RNF: Entries → Outbox → RabbitMQ → Consolidated works end-to-end.
    /// </summary>
    [Fact]
    public async Task CreateCreditEntry_ShouldEventuallyIncreaseConsolidatedBalance()
    {
        var today   = DateTime.UtcNow.Date;
        var amount  = 350.00m;

        // 1. Create entry
        var payload = new
        {
            amount,
            currency    = "BRL",
            type        = 1,                                   // Credit
            description = "E2E — credit entry",
            entryDate   = today.ToString("yyyy-MM-dd")
        };

        var createResp = await _entries.PostAsJsonAsync("/api/entries", payload);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created, "entry creation must succeed");

        // 2. Poll consolidated until TotalCredits ≥ amount (timeout = 15 s)
        //    OutboxProcessor polls every 5 s; consumer is near-instant.
        var balance = await PollUntilAsync(
            async () =>
            {
                var r = await _consolidated.GetAsync($"/api/consolidated/{today:yyyy-MM-dd}");
                if (r.StatusCode == HttpStatusCode.NotFound) return null;
                if (!r.IsSuccessStatusCode) return null;
                return await r.Content.ReadFromJsonAsync<DailyBalanceResponse>();
            },
            predicate: b => b?.TotalCredits >= amount,
            timeoutSeconds: 15);

        balance.Should().NotBeNull("consolidated must receive the event within 15 s");
        balance!.TotalCredits.Should().BeGreaterThanOrEqualTo(amount);
        balance.MerchantId.Should().Be(_merchantId);
        balance.Currency.Should().Be("BRL");
    }

    /// <summary>
    /// Proves that debits are also propagated correctly.
    /// </summary>
    [Fact]
    public async Task CreateDebitEntry_ShouldEventuallyIncreaseTotalDebits()
    {
        var today  = DateTime.UtcNow.Date;
        var amount = 120.00m;

        var payload = new
        {
            amount,
            currency    = "BRL",
            type        = 2,                                   // Debit
            description = "E2E — debit entry",
            entryDate   = today.ToString("yyyy-MM-dd")
        };

        var createResp = await _entries.PostAsJsonAsync("/api/entries", payload);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var balance = await PollUntilAsync(
            async () =>
            {
                var r = await _consolidated.GetAsync($"/api/consolidated/{today:yyyy-MM-dd}");
                if (!r.IsSuccessStatusCode) return null;
                return await r.Content.ReadFromJsonAsync<DailyBalanceResponse>();
            },
            predicate: b => b?.TotalDebits >= amount,
            timeoutSeconds: 15);

        balance.Should().NotBeNull("debit must appear in consolidated within 15 s");
        balance!.TotalDebits.Should().BeGreaterThanOrEqualTo(amount);
    }

    /// <summary>
    /// Proves RNF1: even without a token, the Consolidated service returns 401.
    /// (Gateway would enforce this in production; the service enforces it independently.)
    /// </summary>
    [Fact]
    public async Task Consolidated_WithoutToken_ShouldReturn401()
    {
        var today = DateTime.UtcNow.Date;
        var anonClient = new HttpClient { BaseAddress = _consolidated.BaseAddress };
        var response = await anonClient.GetAsync($"/api/consolidated/{today:yyyy-MM-dd}");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<T?> PollUntilAsync<T>(
        Func<Task<T?>> action,
        Func<T?, bool> predicate,
        int timeoutSeconds) where T : class
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        while (DateTime.UtcNow < deadline)
        {
            var result = await action();
            if (predicate(result)) return result;
            await Task.Delay(500);
        }
        return null;
    }

    private static string GenerateToken(Guid merchantId)
    {
        var key         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(E2ESharedFixture.JwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, merchantId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim("role", "merchant")
        };
        var token = new JwtSecurityToken(
            E2ESharedFixture.JwtIssuer,
            E2ESharedFixture.JwtAudience,
            claims,
            expires: DateTime.UtcNow.AddHours(1),
            signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Local DTO to avoid referencing both assemblies' DailyBalanceDto
    private sealed record DailyBalanceResponse(
        Guid MerchantId,
        DateTime Date,
        decimal TotalCredits,
        decimal TotalDebits,
        decimal Balance,
        string Currency);
}
