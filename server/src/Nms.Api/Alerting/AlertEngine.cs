using Microsoft.EntityFrameworkCore;
using Nms.Api.Contracts;
using Nms.Api.Data;
using Nms.Api.Domain;
using Nms.Api.Hubs;
using Nms.Api.Monitoring;

namespace Nms.Api.Alerting;

public class AlertEngine(
    NmsDbContext db,
    BreachTracker tracker,
    NotificationDispatcher notifications,
    MonitoringBroadcaster broadcaster,
    ILogger<AlertEngine> log)
{
    public async Task EvaluateAsync(
        Device device,
        IReadOnlyList<AlertRule> rules,
        DeviceSample sample,
        IReadOnlyList<(DeviceInterface Interface, InterfaceReading Reading)> interfaces,
        DateTimeOffset now,
        CancellationToken ct)
    {
        foreach (var rule in rules.Where(r => r.Enabled && r.AppliesTo(device)))
        {
            if (rule.Metric == AlertMetric.InterfaceUtilization)
            {
                foreach (var (iface, reading) in interfaces)
                {
                    await EvaluateOneAsync(device, rule, reading.UtilizationPct, iface, now, ct);
                }

                continue;
            }

            var value = ReadMetric(rule.Metric, sample);
            if (value is not null)
            {
                await EvaluateOneAsync(device, rule, value.Value, null, now, ct);
            }
        }
    }

    public async Task<Alert?> AcknowledgeAsync(Guid alertId, string actor, string? note, CancellationToken ct)
    {
        var alert = await db.Alerts.Include(a => a.Device).FirstOrDefaultAsync(a => a.Id == alertId, ct);
        if (alert is null || alert.State != AlertState.Active)
        {
            return alert;
        }

        alert.State = AlertState.Acknowledged;
        alert.AcknowledgedAt = DateTimeOffset.UtcNow;
        alert.AcknowledgedBy = actor;
        AddEvent(alert, AlertEventKind.Acknowledged, note ?? "Acknowledged", actor);

        await db.SaveChangesAsync(ct);
        await broadcaster.AlertChanged(Map.Alert(alert, alert.Device?.Name ?? "Unknown"));
        return alert;
    }

    public async Task<Alert?> ResolveAsync(Guid alertId, string actor, string? note, CancellationToken ct)
    {
        var alert = await db.Alerts.Include(a => a.Device).FirstOrDefaultAsync(a => a.Id == alertId, ct);
        if (alert is null || alert.State == AlertState.Resolved)
        {
            return alert;
        }

        CloseAlert(alert, actor, note ?? "Resolved manually");
        await db.SaveChangesAsync(ct);

        if (alert.RuleId is not null)
        {
            tracker.Clear(new BreachKey(alert.RuleId.Value, alert.DeviceId, alert.InterfaceId));
        }

        await broadcaster.AlertChanged(Map.Alert(alert, alert.Device?.Name ?? "Unknown"));
        return alert;
    }

    /// Alerts raised by a rule can only clear while that rule still evaluates, so
    /// disabling or deleting one has to close whatever it left open.
    public async Task<int> ReleaseRuleAsync(Guid ruleId, string actor, string reason, CancellationToken ct)
    {
        var open = await db.Alerts
            .Include(a => a.Device)
            .Where(a => a.RuleId == ruleId && a.State != AlertState.Resolved)
            .ToListAsync(ct);

        if (open.Count == 0)
        {
            return 0;
        }

        foreach (var alert in open)
        {
            CloseAlert(alert, actor, reason);
            tracker.Clear(new BreachKey(ruleId, alert.DeviceId, alert.InterfaceId));
        }

        await db.SaveChangesAsync(ct);

        foreach (var alert in open)
        {
            await broadcaster.AlertChanged(Map.Alert(alert, alert.Device?.Name ?? "Unknown"));
        }

        return open.Count;
    }

    public async Task<Alert?> CommentAsync(Guid alertId, string actor, string message, CancellationToken ct)
    {
        var alert = await db.Alerts.Include(a => a.Device).FirstOrDefaultAsync(a => a.Id == alertId, ct);
        if (alert is null)
        {
            return null;
        }

        AddEvent(alert, AlertEventKind.Commented, message, actor);
        await db.SaveChangesAsync(ct);
        return alert;
    }

    private async Task EvaluateOneAsync(
        Device device,
        AlertRule rule,
        double value,
        DeviceInterface? iface,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var ifaceId = iface?.Id;
        var key = new BreachKey(rule.Id, device.Id, ifaceId);
        var open = ifaceId is null
            ? await db.Alerts.FirstOrDefaultAsync(
                a => a.DeviceId == device.Id && a.RuleId == rule.Id && a.InterfaceId == null && a.State != AlertState.Resolved, ct)
            : await db.Alerts.FirstOrDefaultAsync(
                a => a.DeviceId == device.Id && a.RuleId == rule.Id && a.InterfaceId == ifaceId && a.State != AlertState.Resolved, ct);

        if (!rule.IsBreached(value))
        {
            tracker.Clear(key);

            if (open is not null)
            {
                CloseAlert(open, "monitor", $"{rule.Metric} back within threshold ({Format(rule.Metric, value)})");
                await db.SaveChangesAsync(ct);
                await broadcaster.AlertChanged(Map.Alert(open, device.Name, iface?.Name, rule.Name));
            }

            return;
        }

        if (tracker.Observe(key, now) < TimeSpan.FromSeconds(rule.DurationSeconds))
        {
            return;
        }

        if (open is not null)
        {
            open.Occurrences++;
            open.LastSeenAt = now;
            open.ObservedValue = value;

            // Every breach would otherwise write a timeline row; sample them instead.
            if (open.Occurrences % 10 == 0)
            {
                AddEvent(open, AlertEventKind.Reoccurred, $"Still breaching after {open.Occurrences} polls ({Format(rule.Metric, value)})", "monitor");
            }

            await db.SaveChangesAsync(ct);
            await broadcaster.AlertChanged(Map.Alert(open, device.Name, iface?.Name, rule.Name));
            return;
        }

        var alert = new Alert
        {
            Id = Guid.NewGuid(),
            RuleId = rule.Id,
            DeviceId = device.Id,
            InterfaceId = iface?.Id,
            Severity = rule.Severity,
            State = AlertState.Active,
            Title = BuildTitle(rule, device, iface, value),
            Detail = rule.Description,
            Metric = rule.Metric.ToString(),
            ObservedValue = value,
            Threshold = rule.Threshold,
            RaisedAt = now,
            LastSeenAt = now
        };

        AddEvent(alert, AlertEventKind.Raised, $"{rule.Name}: {Format(rule.Metric, value)} (threshold {Format(rule.Metric, rule.Threshold)})", "monitor");
        db.Alerts.Add(alert);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            // The partial unique index on open alerts is the backstop when two
            // poll cycles overlap for the same device.
            log.LogDebug(ex, "Duplicate alert suppressed for {Device}/{Rule}", device.Hostname, rule.Name);
            db.Entry(alert).State = EntityState.Detached;
            return;
        }

        var delivered = await notifications.SendAsync(alert, rule, device.Name, ct);
        if (delivered.Count > 0)
        {
            AddEvent(alert, AlertEventKind.Notified, $"Notified via {string.Join(", ", delivered)}", "monitor");
            await db.SaveChangesAsync(ct);
        }

        await broadcaster.AlertRaised(Map.Alert(alert, device.Name, iface?.Name, rule.Name));
        log.LogInformation("Raised {Severity} alert on {Device}: {Title}", alert.Severity, device.Hostname, alert.Title);
    }

    private void CloseAlert(Alert alert, string actor, string message)
    {
        alert.State = AlertState.Resolved;
        alert.ResolvedAt = DateTimeOffset.UtcNow;
        alert.ResolvedBy = actor;
        AddEvent(alert, AlertEventKind.Resolved, message, actor);
    }

    private void AddEvent(Alert alert, AlertEventKind kind, string? message, string? actor)
    {
        db.AlertEvents.Add(new AlertEvent
        {
            Id = Guid.NewGuid(),
            AlertId = alert.Id,
            Kind = kind,
            Message = message,
            Actor = actor,
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static double? ReadMetric(AlertMetric metric, DeviceSample sample) => metric switch
    {
        AlertMetric.Reachability => sample.Reachable ? 1 : 0,
        AlertMetric.Latency => sample.LatencyMs,
        AlertMetric.PacketLoss => sample.PacketLossPct,
        AlertMetric.Cpu => sample.CpuPct,
        AlertMetric.Memory => sample.MemoryPct,
        _ => null
    };

    private static string BuildTitle(AlertRule rule, Device device, DeviceInterface? iface, double value) => rule.Metric switch
    {
        AlertMetric.Reachability => $"{device.Name} is not responding",
        AlertMetric.InterfaceUtilization => $"{device.Name} {iface?.Name} at {value:0.#}% utilization",
        AlertMetric.Latency => $"{device.Name} latency {value:0.#} ms",
        AlertMetric.PacketLoss => $"{device.Name} packet loss {value:0.#}%",
        AlertMetric.Cpu => $"{device.Name} CPU {value:0.#}%",
        AlertMetric.Memory => $"{device.Name} memory {value:0.#}%",
        _ => $"{device.Name} {rule.Name}"
    };

    private static string Format(AlertMetric metric, double value) => metric switch
    {
        AlertMetric.Latency => $"{value:0.##} ms",
        AlertMetric.Reachability => value >= 1 ? "reachable" : "unreachable",
        _ => $"{value:0.##}%"
    };
}
