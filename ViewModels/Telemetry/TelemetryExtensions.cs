using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Telemetry;

namespace KafkaLens.ViewModels.Telemetry;

public static class TelemetryExtensions
{
    public static void Feature(this ITelemetry telemetry, string feature, string? source = null)
    {
        if (!telemetry.IsEnabled) return;
        telemetry.Track(TelemetrySchema.Events.FeatureUsed, new Dictionary<string, object?>
        {
            ["feature"] = feature,
            ["source"] = source,
        });
    }

    /// <summary>Kind of backend a client talks to; never its name or address.</summary>
    public static string ClientKind(IKafkaLensClient? client)
    {
        var typeName = client?.GetType().Name ?? "";
        if (typeName.Contains("SavedMessages", StringComparison.OrdinalIgnoreCase)) return "saved";
        if (typeName.Contains("Grpc", StringComparison.OrdinalIgnoreCase)) return "grpc";
        if (typeName.Contains("Rest", StringComparison.OrdinalIgnoreCase)) return "rest";
        if (typeName.Contains("Local", StringComparison.OrdinalIgnoreCase)) return "local";
        return "other";
    }

    /// <summary>Maps an exception to a coarse category; never includes the message.</summary>
    public static string ErrorCategory(Exception? error)
    {
        if (error == null) return "ok";
        var name = error.GetType().Name;
        var text = (error.GetType().FullName ?? name) + " " + (error.InnerException?.GetType().Name ?? "");
        if (error is OperationCanceledException) return "cancelled";
        if (error is TimeoutException || text.Contains("Timeout", StringComparison.OrdinalIgnoreCase)) return "timeout";
        if (text.Contains("Auth", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Sasl", StringComparison.OrdinalIgnoreCase) ||
            error is UnauthorizedAccessException) return "auth";
        if (text.Contains("Socket", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Http", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Rpc", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("Kafka", StringComparison.OrdinalIgnoreCase)) return "network";
        return "other";
    }

    public static string PositionName(string? fetchPosition) => fetchPosition switch
    {
        "End" => "end",
        "Start" => "start",
        "Offset" => "offset",
        "Timestamp" => "time",
        _ => "other"
    };

    /// <summary>Formatter names are sent only for built-ins; plugin formatters are reported as "plugin".</summary>
    public static string FormatterName(string? name, IReadOnlySet<string> builtInNames) =>
        name == null ? "none" : builtInNames.Contains(name) ? name : "plugin";
}
