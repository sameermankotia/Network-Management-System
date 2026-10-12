using Microsoft.EntityFrameworkCore;
using Nms.Api.Contracts;
using Nms.Api.Data;
using Nms.Api.Domain;
using Nms.Api.Monitoring;
using Nms.Api.Reporting;

namespace Nms.Api.Endpoints;

public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/dashboard", async (
            NmsDbContext db,
            ReportQueries reports,
            LiveTelemetryCache cache,
            int? hours,
            CancellationToken ct) =>
        {
            var to = DateTimeOffset.UtcNow;
            var window = TimeSpan.FromHours(Math.Clamp(hours ?? 6, 1, 72));
            var from = to - window;
            var bucket = window.TotalHours <= 6 ? 300 : window.TotalHours <= 24 ? 900 : 3600;

            var devices = await db.Devices.AsNoTracking().ToListAsync(ct);
            var alertCounts = await DeviceEndpoints.OpenAlertCountsAsync(db, ct);

            var openAlerts = await db.Alerts
                .AsNoTracking()
                .Include(a => a.Device)
                .Where(a => a.State != AlertState.Resolved)
                .OrderByDescending(a => a.RaisedAt)
                .Take(50)
                .ToListAsync(ct);

            var health = await reports.FleetHealthAsync(to.AddHours(-24), ct);
            var throughput = await reports.FleetThroughputAsync(from, to, bucket, ct);
            var latency = await reports.FleetLatencyAsync(from, to, bucket, ct);
            var sites = await reports.SiteRollupsAsync(to.AddHours(-24), ct);

            var samples = cache.Devices();
            var watchlist = devices
                .Select(d => Map.Device(d, cache.Device(d.Id), alertCounts.GetValueOrDefault(d.Id)))
                .OrderByDescending(d => d.Status == DeviceStatus.Down)
                .ThenByDescending(d => d.OpenAlerts)
                .ThenByDescending(d => d.Status == DeviceStatus.Degraded)
                .ThenByDescending(d => d.LatencyMs ?? 0)
                .Take(6)
                .ToList();

            var overview = new DashboardOverview(
                devices.Count,
                devices.Count(d => d.IsMonitored),
                devices.Count(d => d.Status == DeviceStatus.Up),
                devices.Count(d => d.Status == DeviceStatus.Degraded),
                devices.Count(d => d.Status == DeviceStatus.Down),
                devices.Count(d => d.Status == DeviceStatus.Maintenance),
                openAlerts.Count,
                openAlerts.Count(a => a.Severity == AlertSeverity.Critical),
                openAlerts.Count(a => a.State == AlertState.Active),
                health.AvailabilityPct,
                health.AvgLatencyMs,
                health.PeakThroughputMbps,
                Math.Round(samples.Sum(s => s.InMbps ?? 0), 2),
                Math.Round(samples.Sum(s => s.OutMbps ?? 0), 2),
                cache.LastCycle?.At,
                sites,
                throughput,
                latency,
                openAlerts
                    .OrderByDescending(a => AlertEndpoints.Rank(a.Severity))
                    .ThenByDescending(a => a.RaisedAt)
                    .Take(8)
                    .Select(a => Map.Alert(a, a.Device?.Name ?? "Unknown"))
                    .ToList(),
                watchlist);

            return Results.Ok(overview);
        }).WithTags("Dashboard").RequireAuthorization();

        app.MapGet("/api/monitor/status", (LiveTelemetryCache cache) =>
                Results.Ok(new { lastCycle = cache.LastCycle }))
            .WithTags("Dashboard")
            .RequireAuthorization();
    }
}
