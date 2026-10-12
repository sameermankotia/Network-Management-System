using System.Net.Http.Json;
using Nms.Api.Contracts;
using Nms.Api.Domain;

namespace Nms.Api.Alerting;

public class NotificationDispatcher(
    IHttpClientFactory httpClientFactory,
    IConfiguration config,
    ILogger<NotificationDispatcher> log)
{
    public async Task<IReadOnlyList<string>> SendAsync(Alert alert, AlertRule? rule, string deviceName, CancellationToken ct)
    {
        IReadOnlyList<string> channels = rule?.NotifyChannels ?? new List<string> { "inapp" };
        var delivered = new List<string>();

        foreach (var channel in channels)
        {
            switch (channel.ToLowerInvariant())
            {
                case "inapp":
                    delivered.Add("inapp");
                    break;

                case "email":
                    var recipients = config["Notifications:EmailRecipients"] ?? "noc@campus.edu";
                    log.LogInformation("Email notification for {Alert} on {Device} -> {Recipients}", alert.Title, deviceName, recipients);
                    delivered.Add($"email:{recipients}");
                    break;

                case "webhook":
                    var url = config["Notifications:WebhookUrl"];
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        log.LogDebug("Webhook channel requested but Notifications:WebhookUrl is not configured");
                        break;
                    }

                    if (await PostWebhookAsync(url, alert, deviceName, ct))
                    {
                        delivered.Add("webhook");
                    }

                    break;

                default:
                    log.LogWarning("Unknown notification channel {Channel}", channel);
                    break;
            }
        }

        return delivered;
    }

    private async Task<bool> PostWebhookAsync(string url, Alert alert, string deviceName, CancellationToken ct)
    {
        try
        {
            var client = httpClientFactory.CreateClient("notifications");
            var response = await client.PostAsJsonAsync(url, new
            {
                alert.Id,
                alert.Severity,
                alert.Title,
                alert.Detail,
                device = deviceName,
                metric = alert.Metric,
                value = alert.ObservedValue,
                threshold = alert.Threshold,
                raisedAt = alert.RaisedAt
            }, ct);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            log.LogWarning("Webhook returned {Status} for alert {Alert}", (int)response.StatusCode, alert.Id);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            log.LogWarning(ex, "Webhook delivery failed for alert {Alert}", alert.Id);
        }

        return false;
    }
}
