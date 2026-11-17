namespace KafkaLens.ViewModels.Telemetry;

/// <summary>Bound from the "Telemetry" section of appsettings.json.</summary>
public record TelemetryOptions
{
    /// <summary>Umami-compatible collect endpoint (POST). Must be a host we own.</summary>
    public string Endpoint { get; init; } = "https://t.kafkalens.com/api/send";

    /// <summary>Optional JSON kill-switch: <c>{ "enabled": true }</c>. Missing/unreachable means enabled.</summary>
    public string? RemoteConfigUrl { get; init; } = "https://t.kafkalens.com/config.json";

    /// <summary>Umami website id for the desktop app. Empty disables telemetry entirely.</summary>
    public string WebsiteId { get; init; } = "";

    public string Hostname { get; init; } = "app.kafkalens.com";

    public string PrivacyUrl { get; init; } = "https://kafkalens.com/privacy.html";

    public int FlushIntervalSeconds { get; init; } = 60;
    public int MaxBatchSize { get; init; } = 50;
    public int HeartbeatIntervalSeconds { get; init; } = 300;
    public int ActivitySampleSeconds { get; init; } = 30;
    public int IdleThresholdSeconds { get; init; } = 120;
    public long MaxSpoolBytes { get; init; } = 1_000_000;
    public int MaxSpoolAgeDays { get; init; } = 7;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(WebsiteId) && !string.IsNullOrWhiteSpace(Endpoint);
}
