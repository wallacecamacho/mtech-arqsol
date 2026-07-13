using CashFlow.Entries.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Xunit;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

namespace CashFlow.Entries.IntegrationTests.API;

public class EntriesApiTests : IClassFixture<EntriesWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly EntriesWebApplicationFactory _factory;
    private readonly Guid _merchantId = Guid.NewGuid();

    public EntriesApiTests(EntriesWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken(_merchantId));
    }

    [Fact]
    public async Task POST_Entries_WithValidData_ShouldReturn201()
    {
        var payload = new
        {
            amount = 150.00,
            currency = "BRL",
            type = 1, // Credit
            description = "Venda de produto",
            entryDate = DateTime.UtcNow.Date.ToString("yyyy-MM-dd")
        };

        var response = await _client.PostAsJsonAsync("/api/entries", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task POST_Entries_WithoutToken_ShouldReturn401()
    {
        var unauthClient = _factory.CreateClient();
        var payload = new { amount = 100.0, currency = "BRL", type = 1, description = "test", entryDate = DateTime.UtcNow.Date };

        var response = await unauthClient.PostAsJsonAsync("/api/entries", payload);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_Entries_WithValidDate_ShouldReturn200()
    {
        var response = await _client.GetAsync($"/api/entries?date={DateTime.UtcNow.Date:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_Health_Live_ShouldReturn200()
    {
        var response = await _client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string GenerateTestToken(Guid merchantId)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("INTEGRATION_TEST_KEY_ABCDEFGHIJKLMNOP"));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, merchantId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };
        var token = new JwtSecurityToken("cashflow-gateway", "cashflow-services", claims, expires: DateTime.UtcNow.AddHours(1), signingCredentials: credentials);
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
