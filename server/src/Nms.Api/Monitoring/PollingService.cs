using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Nms.Api.Alerting;
using Nms.Api.Contracts;
using Nms.Api.Data;
using Nms.Api.Domain;
using Nms.Api.Hubs;

namespace Nms.Api.Monitoring;

/// The heartbeat of the system: probes every device that is due, stores the
/// sample, re-derives device and link state, hands the sample to the alert
/// engine and pushes the result to connected clients.
public class PollingService(
    IServiceScopeFactory scopes,
    IDeviceProbe probe,
    IResourceSampler sampler,
    LiveTelemetryCache cache,
    MonitoringBroadcaster broadcaster,
    IConfiguration config,
    ILogger<PollingService> log) : BackgroundService
{
    private readonly TimeSpan _tick = TimeSpan.FromSeconds(config.GetValue("Monitoring:TickSeconds", 5));
    private readonly int _maxParallelProbes = config.GetValue("Monitoring:MaxParallelProbes", 16);
    private readonly int _failuresBeforeDown = config.GetValue("Monitoring:FailuresBeforeDown", 2);
    private readonly double _degradedLatencyMs = config.GetValue("Monitoring:DegradedLatencyMs", 150.0);
    private readonly int _retentionDays = config.GetValue("Monitoring:RetentionDays", 30);

    private readonly Dictionary<Guid, DateTimeOffset> _lastPolled = [];
    private readonly Dictionary<Guid, int> _consecutiveFailures = [];
    private DateTimeOffset _lastPrune = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        log.LogInformation("Poller started with a {Tick}s tick", _tick.TotalSeconds);
        await PrimeCacheAsync(stoppingToken);

        using var timer = new PeriodicTimer(_tick);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Poll cycle failed; retrying on the next tick");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken))
            {
                break;
            }
        }
    }

    private async Task PrimeCacheAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NmsDbContext>();

        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
        var recent = await db.DeviceSamples
            .Where(s => s.SampledAt >= cutoff)
            .ToListAsync(ct);

        foreach (var sample in recent.GroupBy(s => s.DeviceId).Select(g => g.MaxBy(s => s.SampledAt)!))
        {
            cache.Record(sample);
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        var started = Stopwatch.StartNew();
        var now = DateTimeOffset.UtcNow;

        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NmsDbContext>();
        var alerts = scope.ServiceProvider.GetRequiredService<AlertEngine>();

        var devices = await db.Devices
            .Include(d => d.Interfaces)
            .Where(d => d.IsMonitored)
            .ToListAsync(ct);

        var due = devices
            .Where(d => d.IsDueForPoll(now, _lastPolled.TryGetValue(d.Id, out var at) ? at : null))
            .ToList();
        if (due.Count == 0)
        {
            return;
        }

        var rules = await db.AlertRules.Where(r => r.Enabled).ToListAsync(ct);
        var results = await ProbeAllAsync(due, now, ct);

        var statusChanges = new List<DeviceStatusChanged>();
        foreach (var result in results)
        {
            var device = result.Device;
            var sample = new DeviceSample
            {
                DeviceId = device.Id,
                SampledAt = now,
                Reachable = result.Probe.Reachable,
                LatencyMs = result.Probe.LatencyMs,
                PacketLossPct = result.Probe.PacketLossPct,
                CpuPct = result.Probe.Reachable ? result.Resources.CpuPct : null,
                MemoryPct = result.Probe.Reachable ? result.Resources.MemoryPct : null,
                InMbps = result.Probe.Reachable ? result.Resources.InMbps : 0,
                OutMbps = result.Probe.Reachable ? result.Resources.OutMbps : 0
            };

            db.DeviceSamples.Add(sample);

            foreach (var reading in result.Interfaces)
            {
                db.InterfaceSamples.Add(new InterfaceSample
                {
                    InterfaceId = reading.InterfaceId,
                    SampledAt = now,
                    InMbps = result.Probe.Reachable ? reading.InMbps : 0,
                    OutMbps = result.Probe.Reachable ? reading.OutMbps : 0,
                    InErrors = reading.InErrors,
                    OutErrors = reading.OutErrors,
                    UtilizationPct = result.Probe.Reachable ? reading.UtilizationPct : 0
                });
            }

            var previous = device.Status;
            var status = NextStatus(device, sample);
            if (status != previous)
            {
                device.Status = status;
                device.StatusChangedAt = now;
                statusChanges.Add(new DeviceStatusChanged(device.Id, device.Name, status, previous, now));
            }

            if (sample.Reachable)
            {
                device.LastSeenAt = now;
                device.UptimeSeconds += device.PollIntervalSeconds;
            }
            else
            {
                device.UptimeSeconds = 0;
            }

            device.UpdatedAt = now;
            _lastPolled[device.Id] = now;

            cache.Record(sample);
            cache.Record(result.Interfaces);
        }

        await db.SaveChangesAsync(ct);

        foreach (var result in results)
        {
            var pairs = result.Device.Interfaces
                .Join(result.Interfaces, i => i.Id, r => r.InterfaceId, (i, r) => (Interface: i, Reading: r))
                .ToList();

            await alerts.EvaluateAsync(result.Device, rules, cache.Device(result.Device.Id)!, pairs, now, ct);
        }

        foreach (var change in statusChanges)
        {
            await broadcaster.StatusChanged(change);
        }

        foreach (var result in results)
        {
            var sample = cache.Device(result.Device.Id)!;
            await broadcaster.SampleRecorded(new SampleRecorded(
                sample.DeviceId,
                sample.SampledAt,
                sample.Reachable,
                sample.LatencyMs,
                sample.PacketLossPct,
                sample.CpuPct,
                sample.MemoryPct,
                sample.InMbps,
                sample.OutMbps));
        }

        await RefreshLinksAsync(db, ct);
        await PublishCycleAsync(db, devices, results.Count, now, started.ElapsedMilliseconds, ct);
        await PruneAsync(db, now, ct);
    }

    private async Task<List<PollOutcome>> ProbeAllAsync(List<Device> due, DateTimeOffset now, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(_maxParallelProbes);
        var tasks = due.Select(async device =>
        {
            await gate.WaitAsync(ct);
            try
            {
                var probeResult = await probe.ProbeAsync(device, ct);
                var resources = sampler.Sample(device, now);
                var interfaces = sampler.SampleInterfaces(device, device.Interfaces, now);
                return new PollOutcome(device, probeResult, resources, interfaces);
            }
            finally
            {
                gate.Release();
            }
        });

        var outcomes = await Task.WhenAll(tasks);
        return outcomes.ToList();
    }

    private DeviceStatus NextStatus(Device device, DeviceSample sample)
    {
        if (!sample.Reachable)
        {
            var failures = _consecutiveFailures.GetValueOrDefault(device.Id) + 1;
            _consecutiveFailures[device.Id] = failures;
            return failures >= _failuresBeforeDown ? DeviceStatus.Down : DeviceStatus.Degraded;
        }

        _consecutiveFailures[device.Id] = 0;

        var degraded = sample.PacketLossPct > 1
                       || sample.LatencyMs > _degradedLatencyMs
                       || sample.CpuPct > 92
                       || sample.MemoryPct > 95;

        return degraded ? DeviceStatus.Degraded : DeviceStatus.Up;
    }

    private async Task RefreshLinksAsync(NmsDbContext db, CancellationToken ct)
    {
        var links = await db.Links.ToListAsync(ct);
        var statuses = await db.Devices.Select(d => new { d.Id, d.Status }).ToDictionaryAsync(d => d.Id, d => d.Status, ct);
        var changed = new List<Link>();

        foreach (var link in links)
        {
            var a = statuses.GetValueOrDefault(link.ADeviceId, DeviceStatus.Unknown);
            var b = statuses.GetValueOrDefault(link.BDeviceId, DeviceStatus.Unknown);
            var next = (a, b) switch
            {
                (DeviceStatus.Down, _) or (_, DeviceStatus.Down) => LinkStatus.Down,
                (DeviceStatus.Up, DeviceStatus.Up) => LinkStatus.Up,
                (DeviceStatus.Unknown, _) or (_, DeviceStatus.Unknown) => LinkStatus.Unknown,
                _ => LinkStatus.Degraded
            };

            if (next == link.Status)
            {
                continue;
            }

            link.Status = next;
            changed.Add(link);
        }

        if (changed.Count == 0)
        {
            return;
        }

        await db.SaveChangesAsync(ct);

        foreach (var link in changed)
        {
            var utilization = link.AInterfaceId is null ? null : cache.Interface(link.AInterfaceId.Value)?.UtilizationPct;
            await broadcaster.LinkChanged(new LinkStatusChanged(link.Id, link.ADeviceId, link.BDeviceId, link.Status, utilization));
        }
    }

    private async Task PublishCycleAsync(NmsDbContext db, List<Device> devices, int polled, DateTimeOffset now, long durationMs, CancellationToken ct)
    {
        var activeAlerts = await db.Alerts.CountAsync(a => a.State != AlertState.Resolved, ct);
        var samples = cache.Devices();

        var cycle = new CycleCompleted(
            now,
            polled,
            devices.Count(d => d.Status == DeviceStatus.Up),
            devices.Count(d => d.Status == DeviceStatus.Degraded),
            devices.Count(d => d.Status == DeviceStatus.Down),
            activeAlerts,
            Math.Round(samples.Sum(s => s.InMbps ?? 0), 2),
            Math.Round(samples.Sum(s => s.OutMbps ?? 0), 2),
            durationMs);

        cache.RecordCycle(cycle);
        await broadcaster.CycleCompleted(cycle);
    }

    private async Task PruneAsync(NmsDbContext db, DateTimeOffset now, CancellationToken ct)
    {
        if (now - _lastPrune < TimeSpan.FromHours(1))
        {
            return;
        }

        _lastPrune = now;
        var cutoff = now.AddDays(-_retentionDays);

        var deviceRows = await db.DeviceSamples.Where(s => s.SampledAt < cutoff).ExecuteDeleteAsync(ct);
        var interfaceRows = await db.InterfaceSamples.Where(s => s.SampledAt < cutoff).ExecuteDeleteAsync(ct);

        if (deviceRows + interfaceRows > 0)
        {
            log.LogInformation("Pruned {Device} device and {Interface} interface samples older than {Days} days", deviceRows, interfaceRows, _retentionDays);
        }
    }

    private record PollOutcome(Device Device, ProbeResult Probe, ResourceReading Resources, IReadOnlyList<InterfaceReading> Interfaces);
}
