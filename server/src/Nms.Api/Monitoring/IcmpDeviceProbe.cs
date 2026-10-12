using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Nms.Api.Domain;

namespace Nms.Api.Monitoring;

public class IcmpDeviceProbe(IConfiguration config, ILogger<IcmpDeviceProbe> log) : IDeviceProbe
{
    private static readonly int[] FallbackPorts = [443, 22, 80, 161];

    private readonly int _attempts = config.GetValue("Monitoring:PingAttempts", 3);
    private readonly int _timeoutMs = config.GetValue("Monitoring:PingTimeoutMs", 1500);

    public async Task<ProbeResult> ProbeAsync(Device device, CancellationToken ct)
    {
        var replies = new List<double>(_attempts);
        var lost = 0;

        using var ping = new Ping();
        for (var i = 0; i < _attempts && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(device.IpAddress, _timeoutMs);
                if (reply.Status == IPStatus.Success)
                {
                    replies.Add(reply.RoundtripTime);
                }
                else
                {
                    lost++;
                }
            }
            catch (PlatformNotSupportedException)
            {
                return await TcpFallbackAsync(device, ct);
            }
            catch (PingException ex)
            {
                log.LogDebug(ex, "ICMP to {Host} failed, falling back to TCP", device.Hostname);
                return await TcpFallbackAsync(device, ct);
            }
        }

        var sent = replies.Count + lost;
        if (sent == 0)
        {
            return ProbeResult.Unreachable("icmp");
        }

        var loss = 100.0 * lost / sent;
        return replies.Count == 0
            ? ProbeResult.Unreachable("icmp")
            : new ProbeResult(true, Math.Round(replies.Average(), 2), Math.Round(loss, 2), "icmp");
    }

    private async Task<ProbeResult> TcpFallbackAsync(Device device, CancellationToken ct)
    {
        foreach (var port in FallbackPorts)
        {
            using var socket = new TcpClient();
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(_timeoutMs);
                await socket.ConnectAsync(device.IpAddress, port, timeout.Token);
                return new ProbeResult(true, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 2), 0, $"tcp/{port}");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // try the next port
            }
        }

        return ProbeResult.Unreachable("tcp");
    }
}
