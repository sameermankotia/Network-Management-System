using System.Globalization;
using System.Text;
using Nms.Api.Contracts;
using Nms.Api.Reporting;

namespace Nms.Api.Endpoints;

public static class ReportEndpoints
{
    public static void MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/reports").WithTags("Reports").RequireAuthorization();

        group.MapGet("/utilization", async (
            ReportQueries reports,
            int? hours,
            Guid? deviceId,
            string? format,
            CancellationToken ct) =>
        {
            var to = DateTimeOffset.UtcNow;
            var span = TimeSpan.FromHours(Math.Clamp(hours ?? 24, 1, 24 * 90));
            var from = to - span;
            var bucket = BucketFor(span);

            var interfaces = await reports.InterfaceUtilizationAsync(from, to, deviceId, ct);
            var series = await reports.TopInterfaceSeriesAsync(from, to, bucket, 5, ct);
            var talkers = await reports.TopTalkersAsync(from, to, 8, ct);

            if (IsCsv(format))
            {
                return Csv("utilization", ["Device", "Interface", "Speed Mbps", "Avg %", "Peak %", "Avg In Mbps", "Avg Out Mbps", "Errors"],
                    interfaces.Select(row => new object?[]
                    {
                        row.DeviceName, row.InterfaceName, row.SpeedMbps, row.AvgUtilizationPct,
                        row.PeakUtilizationPct, row.AvgInMbps, row.AvgOutMbps, row.Errors
                    }));
            }

            return Results.Ok(new UtilizationReport(
                new ReportWindow(from, to, $"{bucket}s"),
                interfaces,
                series,
                talkers));
        });

        group.MapGet("/availability", async (ReportQueries reports, int? days, string? format, CancellationToken ct) =>
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddDays(-Math.Clamp(days ?? 7, 1, 180));

            var rows = await reports.AvailabilityAsync(from, to, ct);
            var daily = await reports.DailyAvailabilityAsync(from, to, ct);
            var fleet = rows.Count == 0 ? 100 : Math.Round(rows.Average(r => r.AvailabilityPct), 3);

            if (IsCsv(format))
            {
                return Csv("availability", ["Device", "Type", "Site", "Availability %", "Avg latency ms", "Peak latency ms", "Outages", "Downtime"],
                    rows.Select(row => new object?[]
                    {
                        row.DeviceName, row.DeviceType, row.Site, row.AvailabilityPct,
                        row.AvgLatencyMs, row.PeakLatencyMs, row.Outages, row.TotalDowntime
                    }));
            }

            return Results.Ok(new AvailabilityReport(new ReportWindow(from, to, "day"), rows, fleet, daily));
        });

        group.MapGet("/alerts", async (ReportQueries reports, int? days, string? granularity, CancellationToken ct) =>
        {
            var to = DateTimeOffset.UtcNow;
            var from = to.AddDays(-Math.Clamp(days ?? 14, 1, 365));
            var bucket = string.IsNullOrWhiteSpace(granularity) ? "day" : granularity;

            var trend = await reports.AlertTrendAsync(from, to, bucket, ct);
            var rules = await reports.NoisiestRulesAsync(from, to, 8, ct);
            var devices = await reports.NoisiestDevicesAsync(from, to, 8, ct);
            var (mtta, mttr) = await reports.ResponseTimesAsync(from, to, ct);

            return Results.Ok(new AlertReport(new ReportWindow(from, to, bucket), trend, rules, devices, mtta, mttr));
        });
    }

    private static int BucketFor(TimeSpan span) => span.TotalHours switch
    {
        <= 6 => 300,
        <= 24 => 900,
        <= 24 * 7 => 3600,
        _ => 21600
    };

    private static bool IsCsv(string? format) => string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase);

    private static IResult Csv(string name, IReadOnlyList<string> headers, IEnumerable<object?[]> rows)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', headers.Select(Escape)));

        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', row.Select(value => Escape(Stringify(value)))));
        }

        var fileName = $"{name}-{DateTimeOffset.UtcNow:yyyyMMdd-HHmm}.csv";
        return Results.File(Encoding.UTF8.GetBytes(builder.ToString()), "text/csv", fileName);
    }

    private static string Stringify(object? value) => value switch
    {
        null => "",
        double number => number.ToString("0.##", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString(@"d\.hh\:mm\:ss", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    private static string Escape(string value) =>
        value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
