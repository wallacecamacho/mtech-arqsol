using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CashFlow.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    // DEMO ONLY — Replace with an IdP (Keycloak, Azure Entra ID) in production.
    //
    // Demo credentials:
    //   merchant1 / Demo@1234
    //   merchant2 / Demo@5678
    //
    // Passwords are stored as PBKDF2-SHA256 derivatives with a fixed demo salt.
    // Do NOT use fixed salts in production; per-user random salts are required.
    private static readonly IReadOnlyDictionary<string, string> DemoPasswordHashes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["merchant1"] = DeriveKey("Demo@1234"),
            ["merchant2"] = DeriveKey("Demo@5678"),
        };

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("token")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetToken([FromBody] LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized(new { error = "Invalid credentials." });

        if (!DemoPasswordHashes.TryGetValue(request.Username, out var storedHash))
            return Unauthorized(new { error = "Invalid credentials." });

        var inputHash = DeriveKey(request.Password);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(storedHash),
                Convert.FromBase64String(inputHash)))
            return Unauthorized(new { error = "Invalid credentials." });

        // Derive a stable, deterministic MerchantId from the username so the same
        // user always receives the same identity across logins.
        var merchantId = DeterministicGuid(request.Username).ToString();

        var jwtKey = _configuration["Jwt:Key"]!;
        var issuer = _configuration["Jwt:Issuer"];
        var audience = _configuration["Jwt:Audience"];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, merchantId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString()),
            new Claim(ClaimTypes.Name, request.Username),
            new Claim("role", "merchant")
        };

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: credentials);

        var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new TokenResponse(tokenString, merchantId, DateTime.UtcNow.AddHours(8)));
    }

    /// <summary>
    /// DEMO-grade PBKDF2 key derivation using a static salt.
    /// Production: use per-user random salts with Argon2id via a proper IdP.
    /// </summary>
    private static string DeriveKey(string password)
    {
        var salt = Encoding.UTF8.GetBytes("cashflow-demo-static-salt-2026");
        return Convert.ToBase64String(
            Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations: 100_000,
                HashAlgorithmName.SHA256,
                outputLength: 32));
    }

    /// <summary>Produces a deterministic UUID from a username (SHA-256 first 16 bytes).</summary>
    private static Guid DeterministicGuid(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var guidBytes = hash[..16];
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}

public record LoginRequest(string Username, string Password);
public record TokenResponse(string Token, string MerchantId, DateTime ExpiresAt);

