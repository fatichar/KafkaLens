using System.Threading;

namespace KafkaLens.ViewModels.Telemetry;

public interface ITelemetrySink
{
    /// <summary>
    /// Sends one event. Returns false on a transient failure (caller may retry later).
    /// Must not throw except for cancellation.
    /// </summary>
    Task<bool> SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken);
}

/// <summary>Writes events to the diagnostic log instead of the network (debug builds).</summary>
public sealed class LogTelemetrySink : ITelemetrySink
{
    public Task<bool> SendAsync(TelemetryEvent telemetryEvent, CancellationToken cancellationToken)
    {
        Serilog.Log.Debug("Telemetry (not sent): {Event}", telemetryEvent.ToJson());
        return Task.FromResult(true);
    }
}
