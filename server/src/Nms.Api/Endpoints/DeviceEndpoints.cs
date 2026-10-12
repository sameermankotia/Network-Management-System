using System.Net;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nms.Api.Alerting;
using Nms.Api.Auth;
using Nms.Api.Contracts;
using Nms.Api.Data;
using Nms.Api.Domain;
using Nms.Api.Monitoring;
using Nms.Api.Reporting;

namespace Nms.Api.Endpoints;

public static class DeviceEndpoints
{
    public static void MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices").WithTags("Devices").RequireAuthorization();

        group.MapGet("/", async (
            NmsDbContext db,
            LiveTelemetryCache cache,
            string? search,
            DeviceStatus? status,
            DeviceType? type,
            string? site,
            string? tag,
            CancellationToken ct) =>
        {
            var query = db.Devices.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = $"%{search.Trim()}%";
                query = query.Where(d => EF.Functions.ILike(d.Name, term)
                                         || EF.Functions.ILike(d.Hostname, term)
                                         || EF.Functions.ILike(d.Site, term));
            }

            if (status is not null)
            {
                query = query.Where(d => d.Status == status);
            }

            if (type is not null)
            {
                query = query.Where(d => d.DeviceType == type);
            }

            if (!string.IsNullOrWhiteSpace(site))
            {
                query = query.Where(d => d.Site == site);
            }

            if (!string.IsNullOrWhiteSpace(tag))
            {
                var wanted = tag.Trim();
                query = query.Where(d => d.Tags.Contains(wanted));
            }

            var devices = await query.OrderBy(d => d.Name).ToListAsync(ct);
            var alertCounts = await OpenAlertCountsAsync(db, ct);

            return Results.Ok(devices
                .Select(d => Map.Device(d, cache.Device(d.Id), alertCounts.GetValueOrDefault(d.Id)))
                .ToList());
        });

        group.MapGet("/{id:guid}", async (
            Guid id,
            NmsDbContext db,
            LiveTelemetryCache cache,
            ReportQueries reports,
            CancellationToken ct) =>
        {
            var device = await db.Devices
                .AsNoTracking()
                .Include(d => d.Interfaces)
                .FirstOrDefaultAsync(d => d.Id == id, ct);

            if (device is null)
            {
                return Results.NotFound();
            }

            var openAlerts = await db.Alerts.CountAsync(a => a.DeviceId == id && a.State != AlertState.Resolved, ct);
            var neighbors = await NeighborsAsync(db, id, ct);
            var availability = (await reports.AvailabilityAsync(DateTimeOffset.UtcNow.AddHours(-24), DateTimeOffset.UtcNow, ct))
                .FirstOrDefault(row => row.DeviceId == id);

            var interfaces = device.Interfaces
                .OrderBy(i => i.IfIndex)
                .Select(i =>
                {
                    var reading = cache.Interface(i.Id);
                    return new InterfaceSummary(
                        i.Id,
                        i.IfIndex,
                        i.Name,
                        i.Description,
                        i.SpeedMbps,
                        i.AdminUp,
                        i.OperUp,
                        reading?.UtilizationPct,
                        reading?.InMbps,
                        reading?.OutMbps,
                        (reading?.InErrors ?? 0) + (reading?.OutErrors ?? 0));
                })
                .ToList();

            return Results.Ok(new DeviceDetail(
                Map.Device(device, cache.Device(device.Id), openAlerts),
                device.Notes,
                device.OsVersion,
                device.ManagementUrl,
                interfaces,
                neighbors,
                availability?.AvailabilityPct,
                availability?.AvgLatencyMs));
        });

        group.MapGet("/{id:guid}/series", async (
            Guid id,
            ReportQueries reports,
            int? hours,
            int? bucketSeconds,
            CancellationToken ct) =>
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddHours(-Math.Clamp(hours ?? 6, 1, 24 * 30));
            var bucket = Math.Clamp(bucketSeconds ?? 300, 60, 3600);

            return Results.Ok(await reports.DeviceSeriesAsync(id, from, to, bucket, ct));
        });

        group.MapGet("/{id:guid}/alerts", async (Guid id, NmsDbContext db, CancellationToken ct) =>
        {
            var alerts = await db.Alerts
                .AsNoTracking()
                .Include(a => a.Device)
                .Include(a => a.Rule)
                .Where(a => a.DeviceId == id)
                .OrderByDescending(a => a.RaisedAt)
                .Take(100)
                .ToListAsync(ct);

            return Results.Ok(alerts.Select(a => Map.Alert(a, a.Device?.Name ?? "Unknown")).ToList());
        });

        group.MapPost("/", async (SaveDeviceRequest request, NmsDbContext db, CancellationToken ct) =>
        {
            if (!IPAddress.TryParse(request.IpAddress, out var ip))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ipAddress"] = ["Not a valid IPv4 or IPv6 address."]
                });
            }

            if (await db.Devices.AnyAsync(d => d.Hostname == request.Hostname, ct))
            {
                return Results.Conflict(new { message = $"{request.Hostname} is already registered." });
            }

            var device = new Device { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
            Apply(device, request, ip);
            db.Devices.Add(device);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/devices/{device.Id}", Map.Device(device, null, 0));
        }).RequireAuthorization("ManageInventory");

        group.MapPut("/{id:guid}", async (Guid id, SaveDeviceRequest request, NmsDbContext db, LiveTelemetryCache cache, CancellationToken ct) =>
        {
            var device = await db.Devices.FindAsync([id], ct);
            if (device is null)
            {
                return Results.NotFound();
            }

            if (!IPAddress.TryParse(request.IpAddress, out var ip))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["ipAddress"] = ["Not a valid IPv4 or IPv6 address."]
                });
            }

            Apply(device, request, ip);
            await db.SaveChangesAsync(ct);

            return Results.Ok(Map.Device(device, cache.Device(device.Id), 0));
        }).RequireAuthorization("ManageInventory");

        group.MapDelete("/{id:guid}", async (Guid id, NmsDbContext db, LiveTelemetryCache cache, CancellationToken ct) =>
        {
            var removed = await db.Devices.Where(d => d.Id == id).ExecuteDeleteAsync(ct);
            cache.Forget(id);
            return removed == 0 ? Results.NotFound() : Results.NoContent();
        }).RequireAuthorization("ManageInventory");

        group.MapPost("/{id:guid}/maintenance", async (
            Guid id,
            MaintenanceRequest request,
            NmsDbContext db,
            ClaimsPrincipal principal,
            BreachTracker tracker,
            CancellationToken ct) =>
        {
            var device = await db.Devices.FindAsync([id], ct);
            if (device is null)
            {
                return Results.NotFound();
            }

            device.Status = request.InMaintenance ? DeviceStatus.Maintenance : DeviceStatus.Unknown;
            device.StatusChangedAt = DateTimeOffset.UtcNow;
            device.UpdatedAt = DateTimeOffset.UtcNow;
            device.Notes = request.InMaintenance
                ? $"Maintenance window opened by {principal.Actor()} at {DateTimeOffset.UtcNow:u}"
                : device.Notes;

            tracker.ClearDevice(id);
            await db.SaveChangesAsync(ct);

            return Results.Ok(new { device.Id, device.Status });
        }).RequireAuthorization("ManageInventory");

        group.MapPost("/{id:guid}/interfaces", async (Guid id, SaveInterfaceRequest request, NmsDbContext db, CancellationToken ct) =>
        {
            if (!await db.Devices.AnyAsync(d => d.Id == id, ct))
            {
                return Results.NotFound();
            }

            var iface = new DeviceInterface
            {
                Id = Guid.NewGuid(),
                DeviceId = id,
                IfIndex = request.IfIndex,
                Name = request.Name,
                Description = request.Description,
                SpeedMbps = request.SpeedMbps,
                AdminUp = request.AdminUp,
                OperUp = request.OperUp,
                MacAddress = request.MacAddress,
                CreatedAt = DateTimeOffset.UtcNow
            };

            db.Interfaces.Add(iface);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/devices/{id}", new { iface.Id });
        }).RequireAuthorization("ManageInventory");

        group.MapPut("/{id:guid}/interfaces/{interfaceId:guid}", async (
            Guid id,
            Guid interfaceId,
            SaveInterfaceRequest request,
            NmsDbContext db,
            CancellationToken ct) =>
        {
            var iface = await db.Interfaces.FirstOrDefaultAsync(i => i.Id == interfaceId && i.DeviceId == id, ct);
            if (iface is null)
            {
                return Results.NotFound();
            }

            iface.IfIndex = request.IfIndex;
            iface.Name = request.Name;
            iface.Description = request.Description;
            iface.SpeedMbps = request.SpeedMbps;
            iface.AdminUp = request.AdminUp;
            iface.OperUp = request.OperUp;
            iface.MacAddress = request.MacAddress;

            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        }).RequireAuthorization("ManageInventory");

        group.MapDelete("/{id:guid}/interfaces/{interfaceId:guid}", async (Guid id, Guid interfaceId, NmsDbContext db, CancellationToken ct) =>
        {
            var removed = await db.Interfaces.Where(i => i.Id == interfaceId && i.DeviceId == id).ExecuteDeleteAsync(ct);
            return removed == 0 ? Results.NotFound() : Results.NoContent();
        }).RequireAuthorization("ManageInventory");

        group.MapGet("/{id:guid}/diagnostics", async (Guid id, NmsDbContext db, CancellationToken ct) =>
        {
            var runs = await db.DiagnosticRuns
                .AsNoTracking()
                .Where(r => r.DeviceId == id)
                .OrderByDescending(r => r.StartedAt)
                .Take(25)
                .ToListAsync(ct);

            return Results.Ok(runs.Select(Map.Diagnostic).ToList());
        });

        group.MapPost("/{id:guid}/diagnostics", async (
            Guid id,
            DiagnosticRequest request,
            NmsDbContext db,
            DiagnosticRunner runner,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var device = await db.Devices.FindAsync([id], ct);
            if (device is null)
            {
                return Results.NotFound();
            }

            var run = await runner.RunAsync(device, request.Kind, request.Port, principal.Actor(), ct);
            return Results.Ok(Map.Diagnostic(run));
        });
    }

    internal static async Task<Dictionary<Guid, int>> OpenAlertCountsAsync(NmsDbContext db, CancellationToken ct) =>
        await db.Alerts
            .Where(a => a.State != AlertState.Resolved)
            .GroupBy(a => a.DeviceId)
            .Select(g => new { DeviceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DeviceId, x => x.Count, ct);

    private static async Task<List<NeighborSummary>> NeighborsAsync(NmsDbContext db, Guid deviceId, CancellationToken ct)
    {
        var links = await db.Links
            .AsNoTracking()
            .Include(l => l.ADevice)
            .Include(l => l.BDevice)
            .Where(l => l.ADeviceId == deviceId || l.BDeviceId == deviceId)
            .ToListAsync(ct);

        return links
            .Select(l =>
            {
                var other = l.ADeviceId == deviceId ? l.BDevice : l.ADevice;
                return new NeighborSummary(
                    l.Id,
                    other?.Id ?? Guid.Empty,
                    other?.Name ?? "Unknown",
                    other?.Status ?? DeviceStatus.Unknown,
                    l.LinkType,
                    l.CapacityMbps,
                    l.Status);
            })
            .OrderBy(n => n.Name)
            .ToList();
    }

    private static void Apply(Device device, SaveDeviceRequest request, IPAddress ip)
    {
        device.Name = request.Name.Trim();
        device.Hostname = request.Hostname.Trim().ToLowerInvariant();
        device.IpAddress = ip;
        device.DeviceType = request.DeviceType;
        device.Vendor = request.Vendor;
        device.Model = request.Model;
        device.OsVersion = request.OsVersion;
        device.Site = string.IsNullOrWhiteSpace(request.Site) ? "Main Campus" : request.Site.Trim();
        device.Building = request.Building;
        device.Room = request.Room;
        device.ManagementUrl = request.ManagementUrl;
        device.PollIntervalSeconds = Math.Clamp(request.PollIntervalSeconds, 10, 3600);
        device.IsMonitored = request.IsMonitored;
        device.Tags = request.Tags?.Select(t => t.Trim()).Where(t => t.Length > 0).Distinct().ToList() ?? [];
        device.Notes = request.Notes;
        device.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
