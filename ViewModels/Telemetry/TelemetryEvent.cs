using System.Text.Json;
using System.Text.Json.Serialization;

namespace KafkaLens.ViewModels.Telemetry;

public sealed record TelemetryEvent(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ts")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("install_id")] string InstallId,
    [property: JsonPropertyName("props")] IReadOnlyDictionary<string, object?> Properties)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static TelemetryEvent? FromJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var props = new Dictionary<string, object?>();
            if (root.TryGetProperty("props", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in p.EnumerateObject())
                    props[prop.Name] = ToPrimitive(prop.Value);
            }

            return new TelemetryEvent(
                root.GetProperty("name").GetString() ?? "",
                root.GetProperty("ts").GetDateTimeOffset(),
                root.GetProperty("install_id").GetString() ?? "",
                props);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static object? ToPrimitive(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Number => e.TryGetInt64(out var l) ? l : e.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null
    };
}
