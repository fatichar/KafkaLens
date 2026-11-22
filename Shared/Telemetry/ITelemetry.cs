using System.Collections.Generic;

namespace KafkaLens.Shared.Telemetry;

/// <summary>
/// Anonymous product telemetry. Implementations must be non-blocking and must never throw.
/// Only enumerated / bucketed values may be passed as properties — never cluster, topic or
/// message data. See docs/analytics-plan.md.
/// </summary>
public interface ITelemetry
{
    bool IsEnabled { get; }

    void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null);
}

public sealed class NullTelemetry : ITelemetry
{
    public static readonly NullTelemetry Instance = new();

    private NullTelemetry() { }

    public bool IsEnabled => false;

    public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null) { }
}
