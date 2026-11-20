namespace KafkaLens.ViewModels.Telemetry;

/// <summary>Coarse telemetry buckets and whole-unit elapsed times.</summary>
public static class TelemetryBuckets
{
    public static string Count(long value) => value switch
    {
        <= 0 => "0",
        1 => "1",
        <= 5 => "2-5",
        <= 10 => "6-10",
        <= 50 => "11-50",
        <= 100 => "51-100",
        <= 500 => "101-500",
        <= 1_000 => "501-1k",
        <= 5_000 => "1k-5k",
        <= 10_000 => "5k-10k",
        <= 50_000 => "10k-50k",
        _ => "50k+"
    };

    public static string Duration(TimeSpan duration) => duration.TotalMilliseconds switch
    {
        < 100 => "<100ms",
        < 500 => "<500ms",
        < 1_000 => "<1s",
        < 3_000 => "<3s",
        < 10_000 => "<10s",
        < 30_000 => "<30s",
        < 60_000 => "<1m",
        _ => "1m+"
    };

    public static long Milliseconds(TimeSpan duration) => Math.Max(0, (long)Math.Round(duration.TotalMilliseconds));

    /// <summary>Rounds seconds to whole numbers, capped to avoid outliers from sleep/hibernate.</summary>
    public static long Seconds(TimeSpan duration, long cap = 24 * 3600) =>
        Math.Clamp((long)Math.Round(duration.TotalSeconds), 0, cap);
}
