using Nms.Api.Contracts;
using Nms.Api.Domain;

namespace Nms.Api.Reporting;

public class ReportQueries(SqlQuery sql)
{
    public Task<List<NamedSeries>> FleetThroughputAsync(DateTimeOffset from, DateTimeOffset to, int bucketSeconds, CancellationToken ct) =>
        BuildPairAsync(
            """
            select bucket,
                   sum(avg_in)  as first_value,
                   sum(avg_out) as second_value
            from (
                select device_id,
                       to_timestamp(floor(extract(epoch from sampled_at) / @bucket) * @bucket) as bucket,
                       avg(in_mbps)  as avg_in,
                       avg(out_mbps) as avg_out
                from device_samples
                where sampled_at between @from and @to
                group by 1, 2
            ) per_device
            group by bucket
            order by bucket
            """,
            "Inbound",
            "Outbound",
            from, to, bucketSeconds, ct);

    public Task<List<NamedSeries>> FleetLatencyAsync(DateTimeOffset from, DateTimeOffset to, int bucketSeconds, CancellationToken ct) =>
        BuildPairAsync(
            """
            select to_timestamp(floor(extract(epoch from sampled_at) / @bucket) * @bucket) as bucket,
                   avg(latency_ms) as first_value,
                   max(latency_ms) as second_value
            from device_samples
            where sampled_at between @from and @to and reachable
            group by 1
            order by 1
            """,
            "Average",
            "Peak",
            from, to, bucketSeconds, ct);

    public async Task<List<NamedSeries>> DeviceSeriesAsync(Guid deviceId, DateTimeOffset from, DateTimeOffset to, int bucketSeconds, CancellationToken ct)
    {
        var rows = await sql.ListAsync(
            """
            select to_timestamp(floor(extract(epoch from sampled_at) / @bucket) * @bucket) as bucket,
                   avg(latency_ms)      as latency,
                   avg(packet_loss_pct) as loss,
                   avg(cpu_pct)         as cpu,
                   avg(memory_pct)      as memory,
                   avg(in_mbps)         as in_mbps,
                   avg(out_mbps)        as out_mbps,
                   100.0 * count(*) filter (where reachable) / count(*) as availability
            from device_samples
            where device_id = @device and sampled_at between @from and @to
            group by 1
            order by 1
            """,
            r => new
            {
                At = r.Timestamp("bucket"),
                Latency = r.Double("latency"),
                Loss = r.Double("loss"),
                Cpu = r.Double("cpu"),
                Memory = r.Double("memory"),
                In = r.Double("in_mbps"),
                Out = r.Double("out_mbps"),
                Availability = r.Double("availability")
            },
            Params(from, to, bucketSeconds, ("@device", deviceId)),
            ct);

        return
        [
            new NamedSeries("Latency", rows.Select(r => new TimeSeriesPoint(r.At, Round(r.Latency))).ToList()),
            new NamedSeries("Packet loss", rows.Select(r => new TimeSeriesPoint(r.At, Round(r.Loss))).ToList()),
            new NamedSeries("CPU", rows.Select(r => new TimeSeriesPoint(r.At, Round(r.Cpu))).ToList()),
            new NamedSeries("Memory", rows.Select(r => new TimeSeriesPoint(r.At, Round(r.Memory))).ToList()),
            new NamedSeries("Inbound", rows.Select(r => new TimeSeriesPoint(r.At, Round(r.In))).ToList()),
            new NamedSeries("Outbound", rows.Select(r => new TimeSeriesPoint(r.At, Round(r.Out))).ToList()),
            new NamedSeries("Availability", rows.Select(r => new TimeSeriesPoint(r.At, Round(r.Availability))).ToList())
        ];
    }

    public Task<List<SiteRollup>> SiteRollupsAsync(DateTimeOffset since, CancellationToken ct) => sql.ListAsync(
        """
        select d.site,
               count(*)                                                as devices,
               count(*) filter (where d.status = 'Up')                 as up,
               count(*) filter (where d.status = 'Degraded')            as degraded,
               count(*) filter (where d.status = 'Down')               as down,
               coalesce(a.open_alerts, 0)                              as open_alerts,
               coalesce(s.availability_pct, 100)                        as availability_pct
        from devices d
        left join (
            select dv.site, count(*) as open_alerts
            from alerts al
            join devices dv on dv.id = al.device_id
            where al.state <> 'Resolved'
            group by dv.site
        ) a on a.site = d.site
        left join (
            select dv.site,
                   100.0 * count(*) filter (where ds.reachable) / nullif(count(*), 0) as availability_pct
            from device_samples ds
            join devices dv on dv.id = ds.device_id
            where ds.sampled_at >= @since
            group by dv.site
        ) s on s.site = d.site
        group by d.site, a.open_alerts, s.availability_pct
        order by d.site
        """,
        r => new SiteRollup(
            r.Text("site"),
            r.IntOrZero("devices"),
            r.IntOrZero("up"),
            r.IntOrZero("degraded"),
            r.IntOrZero("down"),
            r.IntOrZero("open_alerts"),
            Math.Round(r.DoubleOrZero("availability_pct"), 3)),
        new Dictionary<string, object?> { ["@since"] = since },
        ct);

    public async Task<(double AvailabilityPct, double AvgLatencyMs, double PeakThroughputMbps)> FleetHealthAsync(DateTimeOffset since, CancellationToken ct)
    {
        var row = await sql.SingleAsync(
            """
            select coalesce(100.0 * count(*) filter (where reachable) / nullif(count(*), 0), 100) as availability_pct,
                   coalesce(avg(latency_ms) filter (where reachable), 0)                          as avg_latency_ms,
                   coalesce(max(in_mbps + out_mbps), 0)                                           as peak_throughput
            from device_samples
            where sampled_at >= @since
            """,
            r => (
                Math.Round(r.DoubleOrZero("availability_pct"), 3),
                Math.Round(r.DoubleOrZero("avg_latency_ms"), 2),
                Math.Round(r.DoubleOrZero("peak_throughput"), 2)),
            new Dictionary<string, object?> { ["@since"] = since },
            ct);

        return row;
    }

    public Task<List<UtilizationRow>> InterfaceUtilizationAsync(DateTimeOffset from, DateTimeOffset to, Guid? deviceId, CancellationToken ct) => sql.ListAsync(
        """
        select i.id            as interface_id,
               i.device_id     as device_id,
               d.name          as device_name,
               i.name          as interface_name,
               i.speed_mbps    as speed_mbps,
               avg(s.utilization_pct)  as avg_utilization_pct,
               max(s.utilization_pct)  as peak_utilization_pct,
               avg(s.in_mbps)          as avg_in_mbps,
               avg(s.out_mbps)         as avg_out_mbps,
               sum(s.in_errors + s.out_errors) as errors
        from interface_samples s
        join device_interfaces i on i.id = s.interface_id
        join devices d on d.id = i.device_id
        where s.sampled_at between @from and @to
          and (@device::uuid is null or i.device_id = @device::uuid)
        group by i.id, i.device_id, d.name, i.name, i.speed_mbps
        order by avg(s.utilization_pct) desc
        """,
        r => new UtilizationRow(
            r.Guid("interface_id"),
            r.Guid("device_id"),
            r.Text("device_name"),
            r.Text("interface_name"),
            r.IntOrZero("speed_mbps"),
            Math.Round(r.DoubleOrZero("avg_utilization_pct"), 2),
            Math.Round(r.DoubleOrZero("peak_utilization_pct"), 2),
            Math.Round(r.DoubleOrZero("avg_in_mbps"), 2),
            Math.Round(r.DoubleOrZero("avg_out_mbps"), 2),
            r.LongOrZero("errors")),
        new Dictionary<string, object?> { ["@from"] = from, ["@to"] = to, ["@device"] = deviceId },
        ct);

    public Task<List<TopTalkerRow>> TopTalkersAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct) => sql.ListAsync(
        $"""
        select d.id as device_id,
               d.name as device_name,
               avg(s.in_mbps)  as avg_in_mbps,
               avg(s.out_mbps) as avg_out_mbps,
               max(s.in_mbps + s.out_mbps) as peak_mbps
        from device_samples s
        join devices d on d.id = s.device_id
        where s.sampled_at between @from and @to
        group by d.id, d.name
        order by avg(s.in_mbps + s.out_mbps) desc nulls last
        limit {Clamp(limit)}
        """,
        r => new TopTalkerRow(
            r.Guid("device_id"),
            r.Text("device_name"),
            Math.Round(r.DoubleOrZero("avg_in_mbps"), 2),
            Math.Round(r.DoubleOrZero("avg_out_mbps"), 2),
            Math.Round(r.DoubleOrZero("peak_mbps"), 2)),
        new Dictionary<string, object?> { ["@from"] = from, ["@to"] = to },
        ct);

    public Task<List<NamedSeries>> TopInterfaceSeriesAsync(DateTimeOffset from, DateTimeOffset to, int bucketSeconds, int limit, CancellationToken ct) =>
        BuildGroupedAsync(
            $"""
            with busiest as (
                select interface_id
                from interface_samples
                where sampled_at between @from and @to
                group by interface_id
                order by avg(utilization_pct) desc nulls last
                limit {Clamp(limit)}
            )
            select d.name || ' · ' || i.name as series,
                   to_timestamp(floor(extract(epoch from s.sampled_at) / @bucket) * @bucket) as bucket,
                   avg(s.utilization_pct) as value
            from interface_samples s
            join busiest b on b.interface_id = s.interface_id
            join device_interfaces i on i.id = s.interface_id
            join devices d on d.id = i.device_id
            where s.sampled_at between @from and @to
            group by 1, 2
            order by 2
            """,
            Params(from, to, bucketSeconds),
            ct);

    public Task<List<UptimeRow>> AvailabilityAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => sql.ListAsync(
        """
        with flagged as (
            select s.device_id,
                   s.sampled_at,
                   s.reachable,
                   s.latency_ms,
                   lag(s.reachable) over (partition by s.device_id order by s.sampled_at) as previous_reachable
            from device_samples s
            where s.sampled_at between @from and @to
        )
        select d.id as device_id,
               d.name as device_name,
               d.device_type as device_type,
               d.site as site,
               d.poll_interval_seconds as poll_interval_seconds,
               100.0 * count(*) filter (where f.reachable) / nullif(count(*), 0) as availability_pct,
               avg(f.latency_ms) filter (where f.reachable) as avg_latency_ms,
               max(f.latency_ms) as peak_latency_ms,
               count(*) filter (where not f.reachable and coalesce(f.previous_reachable, true)) as outages,
               count(*) filter (where not f.reachable) as down_samples
        from flagged f
        join devices d on d.id = f.device_id
        group by d.id, d.name, d.device_type, d.site, d.poll_interval_seconds
        order by availability_pct nulls last, d.name
        """,
        r => new UptimeRow(
            r.Guid("device_id"),
            r.Text("device_name"),
            Enum.TryParse<DeviceType>(r.Text("device_type"), out var type) ? type : DeviceType.Switch,
            r.Text("site"),
            Math.Round(r.Double("availability_pct") ?? 100, 3),
            Round(r.Double("avg_latency_ms")),
            Round(r.Double("peak_latency_ms")),
            r.IntOrZero("outages"),
            TimeSpan.FromSeconds(r.LongOrZero("down_samples") * r.IntOrZero("poll_interval_seconds"))),
        new Dictionary<string, object?> { ["@from"] = from, ["@to"] = to },
        ct);

    public Task<List<NamedSeries>> DailyAvailabilityAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        BuildGroupedAsync(
            """
            select d.site as series,
                   date_trunc('day', s.sampled_at) as bucket,
                   100.0 * count(*) filter (where s.reachable) / nullif(count(*), 0) as value
            from device_samples s
            join devices d on d.id = s.device_id
            where s.sampled_at between @from and @to
            group by 1, 2
            order by 2
            """,
            new Dictionary<string, object?> { ["@from"] = from, ["@to"] = to },
            ct);

    public Task<List<AlertTrendRow>> AlertTrendAsync(DateTimeOffset from, DateTimeOffset to, string granularity, CancellationToken ct) => sql.ListAsync(
        $"""
        select date_trunc('{Sanitize(granularity)}', raised_at) as bucket,
               count(*) filter (where severity = 'Info')     as info,
               count(*) filter (where severity = 'Warning')  as warning,
               count(*) filter (where severity = 'Major')    as major,
               count(*) filter (where severity = 'Critical') as critical
        from alerts
        where raised_at between @from and @to
        group by 1
        order by 1
        """,
        r => new AlertTrendRow(
            r.Timestamp("bucket"),
            r.IntOrZero("info"),
            r.IntOrZero("warning"),
            r.IntOrZero("major"),
            r.IntOrZero("critical")),
        new Dictionary<string, object?> { ["@from"] = from, ["@to"] = to },
        ct);

    public Task<List<RuleNoiseRow>> NoisiestRulesAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct) => sql.ListAsync(
        $"""
        select a.rule_id as rule_id,
               coalesce(r.name, 'Ad hoc') as rule_name,
               count(*) as alerts,
               count(*) filter (where a.state <> 'Resolved') as still_open
        from alerts a
        left join alert_rules r on r.id = a.rule_id
        where a.raised_at between @from and @to
        group by a.rule_id, r.name
        order by count(*) desc
        limit {Clamp(limit)}
        """,
        r =>
        {
            var index = r.GetOrdinal("rule_id");
            return new RuleNoiseRow(
                r.IsDBNull(index) ? null : r.GetGuid(index),
                r.Text("rule_name"),
                r.IntOrZero("alerts"),
                r.IntOrZero("still_open"));
        },
        new Dictionary<string, object?> { ["@from"] = from, ["@to"] = to },
        ct);

    public Task<List<DeviceNoiseRow>> NoisiestDevicesAsync(DateTimeOffset from, DateTimeOffset to, int limit, CancellationToken ct) => sql.ListAsync(
        $"""
        select d.id as device_id,
               d.name as device_name,
               count(*) as alerts,
               max(case a.severity
                       when 'Critical' then 4
                       when 'Major' then 3
                       when 'Warning' then 2
                       else 1
                   end) as worst
        from alerts a
        join devices d on d.id = a.device_id
        where a.raised_at between @from and @to
        group by d.id, d.name
        order by count(*) desc
        limit {Clamp(limit)}
        """,
        r => new DeviceNoiseRow(
            r.Guid("device_id"),
            r.Text("device_name"),
            r.IntOrZero("alerts"),
            r.IntOrZero("worst") switch
            {
                4 => AlertSeverity.Critical,
                3 => AlertSeverity.Major,
                2 => AlertSeverity.Warning,
                _ => AlertSeverity.Info
            }),
        new Dictionary<string, object?> { ["@from"] = from, ["@to"] = to },
        ct);

    public async Task<(double Mtta, double Mttr)> ResponseTimesAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var row = await sql.SingleAsync(
            """
            select coalesce(avg(extract(epoch from (acknowledged_at - raised_at)) / 60), 0) as mtta,
                   coalesce(avg(extract(epoch from (resolved_at - raised_at)) / 60), 0)     as mttr
            from alerts
            where raised_at between @from and @to
            """,
            r => (Math.Round(r.DoubleOrZero("mtta"), 1), Math.Round(r.DoubleOrZero("mttr"), 1)),
            new Dictionary<string, object?> { ["@from"] = from, ["@to"] = to },
            ct);

        return row;
    }

    private async Task<List<NamedSeries>> BuildPairAsync(
        string query,
        string firstName,
        string secondName,
        DateTimeOffset from,
        DateTimeOffset to,
        int bucketSeconds,
        CancellationToken ct)
    {
        var rows = await sql.ListAsync(
            query,
            r => (At: r.Timestamp("bucket"), First: r.Double("first_value"), Second: r.Double("second_value")),
            Params(from, to, bucketSeconds),
            ct);

        return
        [
            new NamedSeries(firstName, rows.Select(r => new TimeSeriesPoint(r.At, Round(r.First))).ToList()),
            new NamedSeries(secondName, rows.Select(r => new TimeSeriesPoint(r.At, Round(r.Second))).ToList())
        ];
    }

    private async Task<List<NamedSeries>> BuildGroupedAsync(string query, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct)
    {
        var rows = await sql.ListAsync(
            query,
            r => (Series: r.Text("series"), At: r.Timestamp("bucket"), Value: r.Double("value")),
            parameters,
            ct);

        return rows
            .GroupBy(r => r.Series)
            .Select(g => new NamedSeries(g.Key, g.Select(r => new TimeSeriesPoint(r.At, Round(r.Value))).ToList()))
            .OrderBy(s => s.Name)
            .ToList();
    }

    private static Dictionary<string, object?> Params(DateTimeOffset from, DateTimeOffset to, int bucketSeconds, params (string Name, object? Value)[] extra)
    {
        var parameters = new Dictionary<string, object?>
        {
            ["@from"] = from,
            ["@to"] = to,
            ["@bucket"] = bucketSeconds
        };

        foreach (var (name, value) in extra)
        {
            parameters[name] = value;
        }

        return parameters;
    }

    // LIMIT cannot be parameterised without an explicit cast, and these values
    // are API-controlled integers, so clamp and inline them.
    private static int Clamp(int limit) => Math.Clamp(limit, 1, 100);

    private static double? Round(double? value) => value is null ? null : Math.Round(value.Value, 2);

    // date_trunc takes a literal, so the granularity is whitelisted rather than parameterised.
    private static string Sanitize(string granularity) => granularity.ToLowerInvariant() switch
    {
        "hour" => "hour",
        "week" => "week",
        "month" => "month",
        _ => "day"
    };
}
