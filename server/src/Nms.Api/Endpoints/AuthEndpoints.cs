using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nms.Api.Auth;
using Nms.Api.Contracts;
using Nms.Api.Data;

namespace Nms.Api.Endpoints;

public static class AuthEndpoints
{
    public static void MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async (LoginRequest request, NmsDbContext db, TokenIssuer issuer, CancellationToken ct) =>
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);
            if (user is null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
            {
                return Results.Problem("Invalid email or password.", statusCode: StatusCodes.Status401Unauthorized);
            }

            user.LastLoginAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var (token, expiresAt) = issuer.Issue(user);
            return Results.Ok(new AuthenticatedUser(user.Id, user.Email, user.DisplayName, user.Role, token, expiresAt));
        }).AllowAnonymous();

        group.MapGet("/me", async (ClaimsPrincipal principal, NmsDbContext db, CancellationToken ct) =>
        {
            var id = principal.UserId();
            if (id is null)
            {
                return Results.Unauthorized();
            }

            var user = await db.Users.FindAsync([id.Value], ct);
            return user is null
                ? Results.Unauthorized()
                : Results.Ok(new { user.Id, user.Email, user.DisplayName, user.Role, user.LastLoginAt });
        }).RequireAuthorization();
    }
}
