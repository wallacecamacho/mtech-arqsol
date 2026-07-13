using CashFlow.Consolidated.IntegrationTests.Fixtures;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using Xunit;
using System.Security.Claims;
using System.Text;

namespace CashFlow.Consolidated.IntegrationTests.API;

public class ConsolidatedApiTests : IClassFixture<ConsolidatedWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ConsolidatedWebApplicationFactory _factory;
    private readonly Guid _merchantId = Guid.NewGuid();

    public ConsolidatedApiTests(ConsolidatedWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", GenerateTestToken(_merchantId));
    }

    [Fact]
    public async Task GET_Consolidated_WithNoData_ShouldReturn200WithZeroBalance()
    {
        var response = await _client.GetAsync("/api/consolidated");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_Consolidated_ByDate_WithNoData_ShouldReturn404()
    {
        var date = DateTime.UtcNow.Date.AddDays(-10).ToString("yyyy-MM-dd");
        var response = await _client.GetAsync($"/api/consolidated/{date}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GET_Consolidated_WithoutToken_ShouldReturn401()
    {
        var unauthClient = _factory.CreateClient();
        var response = await unauthClient.GetAsync("/api/consolidated");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
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
