using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nms.Api.Data;
using Nms.Api.Domain;

namespace Nms.Api.Monitoring;

/// On-demand checks an administrator can fire at a device, plus the composite
/// health check the UI uses as a one-click triage button.
public class DiagnosticRunner(
    NmsDbContext db,
    IDeviceProbe probe,
    TopologyService topology,
    LiveTelemetryCache cache,
    IConfiguration config,
    ILogger<DiagnosticRunner> log)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<DiagnosticRun> RunAsync(Device device, DiagnosticKind kind, int? port, string? actor, CancellationToken ct)
    {
        var run = new DiagnosticRun
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            Kind = kind,
            State = DiagnosticState.Running,
            RequestedBy = actor,
            StartedAt = DateTimeOffset.UtcNow
        };

        db.DiagnosticRuns.Add(run);
        await db.SaveChangesAsync(ct);

        try
        {
            var (success, summary, output) = kind switch
            {
                DiagnosticKind.Ping => await PingAsync(device, ct),
                DiagnosticKind.TcpCheck => await TcpCheckAsync(device, port ?? 443, ct),
                DiagnosticKind.TraceRoute => await TraceRouteAsync(device, ct),
                DiagnosticKind.HealthCheck => await HealthCheckAsync(device, ct),
                _ => (false, "Unsupported diagnostic", (object)new { })
            };

            run.State = DiagnosticState.Completed;
            run.Success = success;
            run.Summary = summary;
            run.Output = JsonSerializer.Serialize(output, Json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(ex, "Diagnostic {Kind} failed for {Device}", kind, device.Hostname);
            run.State = DiagnosticState.Failed;
            run.Success = false;
            run.Summary = ex.Message;
            run.Output = JsonSerializer.Serialize(new { error = ex.Message }, Json);
        }

        run.CompletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
        return run;
    }

    private async Task<(bool, string, object)> PingAsync(Device device, CancellationToken ct)
    {
        var result = await probe.ProbeAsync(device, ct);
        var summary = result.Reachable
            ? $"Reachable via {result.Method}, {result.LatencyMs:0.##} ms, {result.PacketLossPct:0.#}% loss"
            : $"No response via {result.Method}";

        return (result.Reachable, summary, new
        {
            target = device.IpAddress.ToString(),
            method = result.Method,
            reachable = result.Reachable,
            latencyMs = result.LatencyMs,
            packetLossPct = result.PacketLossPct
        });
    }

    private async Task<(bool, string, object)> TcpCheckAsync(Device device, int port, CancellationToken ct)
    {
        using var client = new TcpClient();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(config.GetValue("Monitoring:PingTimeoutMs", 1500));
            await client.ConnectAsync(device.IpAddress, port, timeout.Token);

            var elapsed = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);
            return (true, $"Port {port} open in {elapsed} ms", new { port, open = true, connectMs = elapsed });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"Port {port} unreachable: {ex.GetType().Name}", new { port, open = false, error = ex.Message });
        }
    }

    private async Task<(bool, string, object)> TraceRouteAsync(Device device, CancellationToken ct)
    {
        var maxHops = config.GetValue("Monitoring:TraceRouteMaxHops", 12);
        var timeoutMs = config.GetValue("Monitoring:PingTimeoutMs", 1500);
        var hops = new List<object>();

        try
        {
            using var ping = new Ping();
            for (var ttl = 1; ttl <= maxHops && !ct.IsCancellationRequested; ttl++)
            {
                var stopwatch = Stopwatch.StartNew();
                var reply = await ping.SendPingAsync(device.IpAddress, timeoutMs, new byte[32], new PingOptions(ttl, true));
                var rtt = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2);

                hops.Add(new
                {
                    hop = ttl,
                    address = reply.Address?.ToString() ?? "*",
                    rttMs = rtt,
                    status = reply.Status.ToString()
                });

                if (reply.Status is IPStatus.Success)
                {
                    return (true, $"Reached {device.IpAddress} in {ttl} hop(s)", new { source = "icmp", hops });
                }
            }

            return (false, $"Did not reach {device.IpAddress} within {maxHops} hops", new { source = "icmp", hops });
        }
        catch (Exception ex) when (ex is PingException or PlatformNotSupportedException or SocketException)
        {
            // ICMP with a custom TTL needs privileges this process may not have;
            // fall back to the documented topology, which is what operators
            // actually want when they ask "how do we get there".
            log.LogDebug(ex, "ICMP traceroute unavailable, using recorded topology for {Device}", device.Hostname);
            return await TopologyPathAsync(device, ct);
        }
    }

    private async Task<(bool, string, object)> TopologyPathAsync(Device device, CancellationToken ct)
    {
        var core = await db.Devices
            .Where(d => d.DeviceType == DeviceType.Router)
            .OrderBy(d => d.Name)
            .FirstOrDefaultAsync(ct);

        if (core is null || core.Id == device.Id)
        {
            return (false, "No core router recorded to trace from", new { source = "topology", hops = Array.Empty<object>() });
        }

        var path = await topology.FindPathAsync(core.Id, device.Id, ct);
        var hops = path.Hops.Select(h => new
        {
            hop = h.Hop,
            device = h.Name,
            status = h.Status.ToString(),
            link = h.IngressLink?.ToString(),
            utilizationPct = h.UtilizationPct
        }).ToList();

        return (path.Found, path.Found
            ? $"{hops.Count} hop(s) from {core.Name} per recorded topology"
            : path.Message ?? "No path found", new { source = "topology", from = core.Name, hops });
    }

    private async Task<(bool, string, object)> HealthCheckAsync(Device device, CancellationToken ct)
    {
        var probeResult = await probe.ProbeAsync(device, ct);
        var sample = cache.Device(device.Id);
        var since = DateTimeOffset.UtcNow.AddHours(-24);

        var availability = await db.DeviceSamples
            .Where(s => s.DeviceId == device.Id && s.SampledAt >= since)
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Reachable = g.Count(s => s.Reachable)
            })
            .FirstOrDefaultAsync(ct);

        var interfaces = await db.Interfaces.Where(i => i.DeviceId == device.Id).ToListAsync(ct);
        var downInterfaces = interfaces.Where(i => i.AdminUp && !i.OperUp).Select(i => i.Name).ToList();
        var saturated = interfaces
            .Select(i => new { i.Name, Reading = cache.Interface(i.Id) })
            .Where(x => x.Reading is not null && x.Reading.UtilizationPct > 80)
            .Select(x => $"{x.Name} at {x.Reading!.UtilizationPct:0.#}%")
            .ToList();

        var openAlerts = await db.Alerts.CountAsync(a => a.DeviceId == device.Id && a.State != AlertState.Resolved, ct);
        var availabilityPct = availability is null || availability.Total == 0
            ? (double?)null
            : Math.Round(100.0 * availability.Reachable / availability.Total, 3);

        var cpu = sample?.CpuPct;
        var memory = sample?.MemoryPct;

        var checks = new List<CheckResult>
        {
            new("Reachability", probeResult.Reachable, probeResult.Reachable ? $"Responding via {probeResult.Method}" : "No response"),
            new("Latency", probeResult.LatencyMs is null or < 150, $"{probeResult.LatencyMs ?? 0:0.##} ms"),
            new("Packet loss", probeResult.PacketLossPct < 1, $"{probeResult.PacketLossPct:0.#}%"),
            new("CPU", cpu is null or < 85, cpu is null ? "no reading" : $"{cpu:0.#}%"),
            new("Memory", memory is null or < 90, memory is null ? "no reading" : $"{memory:0.#}%"),
            new("Interfaces", downInterfaces.Count == 0, downInterfaces.Count == 0 ? $"{interfaces.Count} up" : $"down: {string.Join(", ", downInterfaces)}"),
            new("Capacity", saturated.Count == 0, saturated.Count == 0 ? "no saturated uplinks" : string.Join(", ", saturated)),
            new("24h availability", availabilityPct is null or >= 99, availabilityPct is null ? "no history" : $"{availabilityPct:0.###}%"),
            new("Open alerts", openAlerts == 0, openAlerts == 0 ? "none" : $"{openAlerts} open")
        };

        var failed = checks.Count(c => !c.Passed);
        var summary = failed == 0
            ? $"All {checks.Count} checks passed"
            : $"{failed} of {checks.Count} checks need attention";

        return (failed == 0, summary, new { checks, availabilityPct, openAlerts });
    }

    private record CheckResult(string Name, bool Passed, string Detail);
}
