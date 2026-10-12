using System.Collections.Concurrent;

namespace Nms.Api.Alerting;

/// Remembers when a rule first went out of bounds so "for N seconds" thresholds
/// mean what they say. Kept in memory on purpose - a restart starts the clock
/// over rather than resurrecting stale breaches.
public class BreachTracker
{
    private readonly ConcurrentDictionary<BreachKey, DateTimeOffset> _since = new();

    public TimeSpan Observe(BreachKey key, DateTimeOffset now)
    {
        var since = _since.GetOrAdd(key, now);
        return now - since;
    }

    public void Clear(BreachKey key) => _since.TryRemove(key, out _);

    public void ClearDevice(Guid deviceId)
    {
        foreach (var key in _since.Keys.Where(k => k.DeviceId == deviceId))
        {
            _since.TryRemove(key, out _);
        }
    }
}

public readonly record struct BreachKey(Guid RuleId, Guid DeviceId, Guid? InterfaceId);
