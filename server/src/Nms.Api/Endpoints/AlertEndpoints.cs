using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Nms.Api.Alerting;
using Nms.Api.Auth;
using Nms.Api.Contracts;
using Nms.Api.Data;
using Nms.Api.Domain;

namespace Nms.Api.Endpoints;

public static class AlertEndpoints
{
    public static void MapAlertEndpoints(this IEndpointRouteBuilder app)
    {
        var alerts = app.MapGroup("/api/alerts").WithTags("Alerts").RequireAuthorization();

        alerts.MapGet("/", async (
            NmsDbContext db,
            AlertState? state,
            AlertSeverity? severity,
            Guid? deviceId,
            int? hours,
            int? limit,
            CancellationToken ct) =>
        {
            var query = db.Alerts.AsNoTracking().Include(a => a.Device).Include(a => a.Rule).AsQueryable();

            if (state is not null)
            {
                query = query.Where(a => a.State == state);
            }
            else
            {
                query = query.Where(a => a.State != AlertState.Resolved);
            }

            if (severity is not null)
            {
                query = query.Where(a => a.Severity == severity);
            }

            if (deviceId is not null)
            {
                query = query.Where(a => a.DeviceId == deviceId);
            }

            if (hours is not null)
            {
                var since = DateTimeOffset.UtcNow.AddHours(-hours.Value);
                query = query.Where(a => a.RaisedAt >= since);
            }

            var rows = await query
                .OrderByDescending(a => a.RaisedAt)
                .Take(Math.Clamp(limit ?? 200, 1, 500))
                .ToListAsync(ct);

            var interfaceNames = await InterfaceNamesAsync(db, rows, ct);

            // Severity is stored as text, so the worst-first ordering the queue
            // needs is applied here rather than alphabetically in SQL.
            return Results.Ok(rows
                .OrderByDescending(a => Rank(a.Severity))
                .ThenByDescending(a => a.RaisedAt)
                .Select(a => Map.Alert(a, a.Device?.Name ?? "Unknown", Name(interfaceNames, a)))
                .ToList());
        });

        alerts.MapGet("/{id:guid}", async (Guid id, NmsDbContext db, CancellationToken ct) =>
        {
            var alert = await db.Alerts
                .AsNoTracking()
                .Include(a => a.Device)
                .Include(a => a.Rule)
                .Include(a => a.Events)
                .FirstOrDefaultAsync(a => a.Id == id, ct);

            if (alert is null)
            {
                return Results.NotFound();
            }

            var timeline = alert.Events
                .OrderBy(e => e.CreatedAt)
                .Select(Map.Event)
                .ToList();

            var interfaceNames = await InterfaceNamesAsync(db, [alert], ct);

            return Results.Ok(new AlertDetail(
                Map.Alert(alert, alert.Device?.Name ?? "Unknown", Name(interfaceNames, alert)),
                timeline));
        });

        alerts.MapPost("/{id:guid}/acknowledge", async (
            Guid id,
            AcknowledgeRequest request,
            AlertEngine engine,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var alert = await engine.AcknowledgeAsync(id, principal.Actor(), request.Note, ct);
            return alert is null ? Results.NotFound() : Results.Ok(Map.Alert(alert, alert.Device?.Name ?? "Unknown"));
        });

        alerts.MapPost("/{id:guid}/resolve", async (
            Guid id,
            ResolveRequest request,
            AlertEngine engine,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var alert = await engine.ResolveAsync(id, principal.Actor(), request.Note, ct);
            return alert is null ? Results.NotFound() : Results.Ok(Map.Alert(alert, alert.Device?.Name ?? "Unknown"));
        });

        alerts.MapPost("/{id:guid}/comments", async (
            Guid id,
            CommentRequest request,
            AlertEngine engine,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Message))
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["message"] = ["Comment cannot be empty."] });
            }

            var alert = await engine.CommentAsync(id, principal.Actor(), request.Message.Trim(), ct);
            return alert is null ? Results.NotFound() : Results.NoContent();
        });

        alerts.MapPost("/acknowledge", async (
            Guid[] ids,
            AlertEngine engine,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var acknowledged = 0;
            foreach (var id in ids.Distinct())
            {
                var alert = await engine.AcknowledgeAsync(id, principal.Actor(), "Bulk acknowledged", ct);
                if (alert?.State == AlertState.Acknowledged)
                {
                    acknowledged++;
                }
            }

            return Results.Ok(new { acknowledged });
        });

        var rules = app.MapGroup("/api/alert-rules").WithTags("Alert rules").RequireAuthorization();

        rules.MapGet("/", async (NmsDbContext db, CancellationToken ct) =>
        {
            var rows = await db.AlertRules.AsNoTracking().OrderBy(r => r.Name).ToListAsync(ct);
            return Results.Ok(rows);
        });

        rules.MapPost("/", async (SaveRuleRequest request, NmsDbContext db, CancellationToken ct) =>
        {
            var problem = Validate(request);
            if (problem is not null)
            {
                return problem;
            }

            var rule = new AlertRule { Id = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow };
            Apply(rule, request);
            db.AlertRules.Add(rule);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/alert-rules/{rule.Id}", rule);
        }).RequireAuthorization("ManageInventory");

        rules.MapPut("/{id:guid}", async (Guid id, SaveRuleRequest request, NmsDbContext db, CancellationToken ct) =>
        {
            var rule = await db.AlertRules.FindAsync([id], ct);
            if (rule is null)
            {
                return Results.NotFound();
            }

            var problem = Validate(request);
            if (problem is not null)
            {
                return problem;
            }

            Apply(rule, request);
            await db.SaveChangesAsync(ct);
            return Results.Ok(rule);
        }).RequireAuthorization("ManageInventory");

        rules.MapPost("/{id:guid}/toggle", async (
            Guid id,
            NmsDbContext db,
            AlertEngine engine,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var rule = await db.AlertRules.FindAsync([id], ct);
            if (rule is null)
            {
                return Results.NotFound();
            }

            rule.Enabled = !rule.Enabled;
            rule.UpdatedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            var released = rule.Enabled
                ? 0
                : await engine.ReleaseRuleAsync(id, principal.Actor(), $"Rule \"{rule.Name}\" was disabled", ct);

            return Results.Ok(new { rule.Id, rule.Enabled, released });
        });

        rules.MapDelete("/{id:guid}", async (
            Guid id,
            NmsDbContext db,
            AlertEngine engine,
            ClaimsPrincipal principal,
            CancellationToken ct) =>
        {
            var rule = await db.AlertRules.FindAsync([id], ct);
            if (rule is null)
            {
                return Results.NotFound();
            }

            await engine.ReleaseRuleAsync(id, principal.Actor(), $"Rule \"{rule.Name}\" was deleted", ct);
            db.AlertRules.Remove(rule);
            await db.SaveChangesAsync(ct);

            return Results.NoContent();
        }).RequireAuthorization("ManageInventory");
    }

    /// Interface-scoped alerts store only the id, so names are resolved in one
    /// lookup rather than by eager-loading a navigation on every read.
    internal static async Task<Dictionary<Guid, string>> InterfaceNamesAsync(
        NmsDbContext db,
        IEnumerable<Alert> alerts,
        CancellationToken ct)
    {
        var ids = alerts.Where(a => a.InterfaceId is not null).Select(a => a.InterfaceId!.Value).Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        return await db.Interfaces
            .Where(i => ids.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.Name, ct);
    }

    internal static int Rank(AlertSeverity severity) => severity switch
    {
        AlertSeverity.Critical => 4,
        AlertSeverity.Major => 3,
        AlertSeverity.Warning => 2,
        _ => 1
    };

    private static string? Name(IReadOnlyDictionary<Guid, string> names, Alert alert) =>
        alert.InterfaceId is null ? null : names.GetValueOrDefault(alert.InterfaceId.Value);

    private static IResult? Validate(SaveRuleRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["A rule needs a name."];
        }

        if (request.DurationSeconds is < 0 or > 86_400)
        {
            errors["durationSeconds"] = ["Duration must be between 0 and 86400 seconds."];
        }

        if (request.TargetKind != RuleTargetKind.All && string.IsNullOrWhiteSpace(request.TargetValue))
        {
            errors["targetValue"] = [$"A {request.TargetKind} rule needs a target value."];
        }

        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    private static void Apply(AlertRule rule, SaveRuleRequest request)
    {
        rule.Name = request.Name.Trim();
        rule.Description = request.Description;
        rule.Metric = request.Metric;
        rule.Comparison = request.Comparison;
        rule.Threshold = request.Threshold;
        rule.DurationSeconds = request.DurationSeconds;
        rule.Severity = request.Severity;
        rule.TargetKind = request.TargetKind;
        rule.TargetValue = string.IsNullOrWhiteSpace(request.TargetValue) ? null : request.TargetValue.Trim();
        rule.Enabled = request.Enabled;
        rule.NotifyChannels = request.NotifyChannels is { Count: > 0 } channels
            ? channels.Select(c => c.Trim().ToLowerInvariant()).Distinct().ToList()
            : new List<string> { "inapp" };
        rule.UpdatedAt = DateTimeOffset.UtcNow;
    }
}
