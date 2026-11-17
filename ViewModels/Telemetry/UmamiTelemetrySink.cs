using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Serilog;

namespace KafkaLens.ViewModels.Telemetry;

/// <summary>
/// Sends events to an Umami-compatible <c>/api/send</c> endpoint.
/// The install id is sent as Umami's distinct id so DAU/WAU/MAU and retention work.
/// </summary>
public sealed class UmamiTelemetrySink : ITelemetrySink
{
    private readonly HttpClient http;
    private readonly TelemetryOptions options;
    private readonly string userAgent;
    private DateTimeOffset retryAfter = DateTimeOffset.MinValue;

    public UmamiTelemetrySink(HttpClient http, TelemetryOptions options, TelemetryEnvironment environment)
    {
        this.http = http;
        this.options = options;
        userAgent = environment.UserAgent;
    }

    public async Task<bool> SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < retryAfter) return false;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, options.Endpoint);
            request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
            request.Content = new StringContent(BuildPayload(telemetryEvent), Encoding.UTF8, "application/json");

            using var response = await http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return true;

            if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
            {
                var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromMinutes(5);
                retryAfter = DateTimeOffset.UtcNow + delay;
                return false;
            }

            // 4xx other than 429: the payload is rejected; retrying won't help.
            Log.Debug("Telemetry event {Event} rejected with {Status}", telemetryEvent.Name, response.StatusCode);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Telemetry send failed");
            retryAfter = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1);
            return false;
        }
    }

    internal string BuildPayload(TelemetryEvent e)
    {
        var props = e.Properties;
        var payload = new Dictionary<string, object?>
        {
            ["website"] = options.WebsiteId,
            ["hostname"] = options.Hostname,
            ["url"] = "/" + e.Name,
            ["title"] = "KafkaLens",
            ["language"] = props.TryGetValue("locale", out var locale) ? locale : null,
            ["name"] = e.Name,
            ["data"] = props,
            ["id"] = e.InstallId,
            ["timestamp"] = e.Timestamp.ToUnixTimeSeconds(),
        };
        return JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["type"] = "event",
            ["payload"] = payload
        });
    }
}
