using Microsoft.EntityFrameworkCore;
using Nms.Api.Contracts;
using Nms.Api.Data;
using Nms.Api.Domain;

namespace Nms.Api.Monitoring;

public class TopologyService(NmsDbContext db, LiveTelemetryCache cache)
{
    public async Task<TopologyGraph> GetGraphAsync(string? site, CancellationToken ct)
    {
        var devices = await db.Devices
            .Where(d => site == null || d.Site == site)
            .OrderBy(d => d.Name)
            .ToListAsync(ct);

        var ids = devices.Select(d => d.Id).ToHashSet();

        var links = await db.Links.ToListAsync(ct);
        links = links.Where(l => ids.Contains(l.ADeviceId) && ids.Contains(l.BDeviceId)).ToList();

        var openAlerts = await db.Alerts
            .Where(a => a.State != AlertState.Resolved)
            .GroupBy(a => a.DeviceId)
            .Select(g => new { DeviceId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.DeviceId, x => x.Count, ct);

        var tiers = ComputeTiers(devices, links);
        var interfaces = await db.Interfaces.Where(i => ids.Contains(i.DeviceId)).ToListAsync(ct);
        var byInterface = interfaces.ToDictionary(i => i.Id);

        var nodes = devices.Select(d =>
        {
            var sample = cache.Device(d.Id);
            return new TopologyNode(
                d.Id,
                d.Name,
                d.Hostname,
                d.DeviceType,
                d.Status,
                d.Site,
                d.Building,
                tiers.GetValueOrDefault(d.Id, 3),
                openAlerts.GetValueOrDefault(d.Id),
                sample?.LatencyMs,
                sample?.CpuPct,
                d.Tags);
        }).ToList();

        var edges = links.Select(l =>
        {
            var reading = EdgeReading(l, byInterface);
            return new TopologyEdge(
                l.Id,
                l.ADeviceId,
                l.BDeviceId,
                l.LinkType,
                l.Status,
                l.CapacityMbps,
                reading?.UtilizationPct,
                reading is null ? null : Math.Round(reading.InMbps + reading.OutMbps, 2));
        }).ToList();

        var sites = await db.Devices.Select(d => d.Site).Distinct().OrderBy(s => s).ToListAsync(ct);

        return new TopologyGraph(nodes, edges, sites);
    }

    public async Task<ConnectivityPath> FindPathAsync(Guid fromDeviceId, Guid toDeviceId, CancellationToken ct)
    {
        if (fromDeviceId == toDeviceId)
        {
            return new ConnectivityPath(false, [], "Source and destination are the same device.");
        }

        var devices = await db.Devices.ToDictionaryAsync(d => d.Id, ct);
        if (!devices.ContainsKey(fromDeviceId) || !devices.ContainsKey(toDeviceId))
        {
            return new ConnectivityPath(false, [], "Unknown device.");
        }

        var links = await db.Links.ToListAsync(ct);
        var adjacency = BuildAdjacency(links);

        var previous = new Dictionary<Guid, (Guid Device, Link Link)>();
        var visited = new HashSet<Guid> { fromDeviceId };
        var queue = new Queue<Guid>();
        queue.Enqueue(fromDeviceId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == toDeviceId)
            {
                break;
            }

            foreach (var (neighbor, link) in adjacency.GetValueOrDefault(current, []))
            {
                if (!visited.Add(neighbor))
                {
                    continue;
                }

                previous[neighbor] = (current, link);
                queue.Enqueue(neighbor);
            }
        }

        if (!visited.Contains(toDeviceId))
        {
            return new ConnectivityPath(false, [], "No documented path between these devices.");
        }

        var interfaces = await db.Interfaces.ToDictionaryAsync(i => i.Id, ct);
        var reversed = new List<PathHop>();
        var cursor = toDeviceId;
        var hopIndex = 0;

        while (true)
        {
            var device = devices[cursor];
            Link? ingress = previous.TryGetValue(cursor, out var step) ? step.Link : null;
            var reading = ingress is null ? null : EdgeReading(ingress, interfaces);

            reversed.Add(new PathHop(hopIndex++, device.Id, device.Name, device.Status, ingress?.LinkType, reading?.UtilizationPct));

            if (cursor == fromDeviceId)
            {
                break;
            }

            cursor = previous[cursor].Device;
        }

        reversed.Reverse();
        var hops = reversed.Select((hop, index) => hop with { Hop = index }).ToList();
        var broken = hops.FirstOrDefault(h => h.Status == DeviceStatus.Down);

        return new ConnectivityPath(true, hops, broken is null ? null : $"Path traverses {broken.Name}, which is currently down.");
    }

    private InterfaceReading? EdgeReading(Link link, IReadOnlyDictionary<Guid, DeviceInterface> interfaces)
    {
        foreach (var id in new[] { link.AInterfaceId, link.BInterfaceId })
        {
            if (id is not null && interfaces.ContainsKey(id.Value) && cache.Interface(id.Value) is { } reading)
            {
                return reading;
            }
        }

        return null;
    }

    private static Dictionary<Guid, List<(Guid Neighbor, Link Link)>> BuildAdjacency(IEnumerable<Link> links)
    {
        var adjacency = new Dictionary<Guid, List<(Guid, Link)>>();

        foreach (var link in links)
        {
            if (link.Status == LinkStatus.Down)
            {
                continue;
            }

            adjacency.TryAdd(link.ADeviceId, []);
            adjacency.TryAdd(link.BDeviceId, []);
            adjacency[link.ADeviceId].Add((link.BDeviceId, link));
            adjacency[link.BDeviceId].Add((link.ADeviceId, link));
        }

        return adjacency;
    }

    /// Tier 0 is the core (routers and firewalls); everything else is its hop
    /// distance from the core, which is what drives the layered layout in the UI.
    private static Dictionary<Guid, int> ComputeTiers(List<Device> devices, List<Link> links)
    {
        var adjacency = BuildAdjacency(links);
        var tiers = new Dictionary<Guid, int>();
        var queue = new Queue<Guid>();

        foreach (var device in devices.Where(d => d.DeviceType is DeviceType.Router or DeviceType.Firewall))
        {
            tiers[device.Id] = 0;
            queue.Enqueue(device.Id);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var (neighbor, _) in adjacency.GetValueOrDefault(current, []))
            {
                if (tiers.ContainsKey(neighbor))
                {
                    continue;
                }

                tiers[neighbor] = tiers[current] + 1;
                queue.Enqueue(neighbor);
            }
        }

        foreach (var device in devices.Where(d => !tiers.ContainsKey(d.Id)))
        {
            tiers[device.Id] = 3;
        }

        return tiers;
    }
}
