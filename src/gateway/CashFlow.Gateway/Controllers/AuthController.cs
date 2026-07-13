using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace CashFlow.Gateway.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    // In-memory demo store: username → stable merchantId.
    // Ensures the same user always gets the same MerchantId across logins.
    // Replace with a proper user store / IdP in production.
    private static readonly ConcurrentDictionary<string, Guid> _merchantIds = new(StringComparer.OrdinalIgnoreCase);

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("token")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public IActionResult GetToken([FromBody] LoginRequest request)
    {
        // Demo-only: in production, validate credentials against a user store / IdP
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return Unauthorized(new { error = "Invalid credentials." });

        // Derive a stable, deterministic MerchantId from the username so the same
        // user always receives the same identity across logins (demo behaviour).
        var merchantId = _merchantIds.GetOrAdd(request.Username, _ => DeterministicGuid(request.Username)).ToString();

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
    /// Produces a deterministic, stable UUID from a string using SHA-256.
    /// Same input always yields the same Guid (version 5 UUID-like behaviour).
    /// </summary>
    private static Guid DeterministicGuid(string input)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        // Use first 16 bytes of hash as UUID bytes
        var guidBytes = hash[..16];
        // Set version bits (version 5 style)
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}

public record LoginRequest(string Username, string Password);
public record TokenResponse(string Token, string MerchantId, DateTime ExpiresAt);
