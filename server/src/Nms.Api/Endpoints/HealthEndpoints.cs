using Microsoft.EntityFrameworkCore;
using Nms.Api.Data;
using Nms.Api.Monitoring;

namespace Nms.Api.Endpoints;

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/health", async (NmsDbContext db, LiveTelemetryCache cache, CancellationToken ct) =>
        {
            var databaseUp = await db.Database.CanConnectAsync(ct);
            var lastCycle = cache.LastCycle;
            var pollerFresh = lastCycle is not null && DateTimeOffset.UtcNow - lastCycle.At < TimeSpan.FromMinutes(5);

            var payload = new
            {
                status = databaseUp && pollerFresh ? "healthy" : databaseUp ? "degraded" : "unhealthy",
                database = databaseUp,
                poller = new
                {
                    running = pollerFresh,
                    lastCycleAt = lastCycle?.At,
                    devicesPolled = lastCycle?.DevicesPolled,
                    durationMs = lastCycle?.DurationMs
                },
                version = typeof(HealthEndpoints).Assembly.GetName().Version?.ToString() ?? "dev"
            };

            return databaseUp ? Results.Ok(payload) : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
        }).WithTags("Health").AllowAnonymous();
    }
}
