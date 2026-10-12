using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Nms.Api.Domain;

namespace Nms.Api.Auth;

public class TokenIssuer(IConfiguration config)
{
    public static SymmetricSecurityKey SigningKey(IConfiguration config) =>
        new(Encoding.UTF8.GetBytes(config["Auth:SigningKey"]
            ?? throw new InvalidOperationException("Auth:SigningKey is not configured")));

    public (string Token, DateTimeOffset ExpiresAt) Issue(User user)
    {
        var expiresAt = DateTimeOffset.UtcNow.AddMinutes(config.GetValue("Auth:TokenLifetimeMinutes", 720));

        var token = new JwtSecurityToken(
            issuer: config["Auth:Issuer"] ?? "nms",
            audience: config["Auth:Audience"] ?? "nms-web",
            claims:
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role)
            ],
            notBefore: DateTime.UtcNow,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256));

        return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt);
    }
}

public static class PrincipalExtensions
{
    public static string Actor(this ClaimsPrincipal principal) =>
        principal.FindFirst(ClaimTypes.Name)?.Value
        ?? principal.FindFirst(ClaimTypes.Email)?.Value
        ?? "unknown";

    public static Guid? UserId(this ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
}
