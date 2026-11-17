using System.Globalization;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Telemetry;

namespace KafkaLens.ViewModels.Telemetry;

/// <summary>
/// Builds the daily <c>settings_snapshot</c> event: enumerated / bucketed setting values plus a
/// <c>*_is_default</c> flag for each. Never includes paths, names, addresses or ids of user data.
/// </summary>
public static class SettingsSnapshot
{
    internal const string LastSnapshotKey = "TelemetryLastSnapshotUtc";
    private const string DefaultTheme = "System";
    private const int DefaultConnectionCheckSeconds = 300;

    public sealed record Inventory(
        int Clusters,
        int LocalClients,
        int GrpcClients,
        int RestClients,
        int PluginsInstalled,
        int PluginsEnabled);

    public static void SendIfDue(ITelemetry telemetry, ISettingsService settings, Inventory inventory,
        DateTimeOffset? now = null)
    {
        if (!telemetry.IsEnabled) return;
        var current = now ?? DateTimeOffset.UtcNow;
        if (DateTimeOffset.TryParse(settings.GetValue(LastSnapshotKey), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var last) && current - last < TimeSpan.FromHours(23))
            return;

        Send(telemetry, settings, inventory, "daily");
        settings.SetValue(LastSnapshotKey, current.ToString("O", CultureInfo.InvariantCulture));
    }

    public static void Send(ITelemetry telemetry, ISettingsService settings, Inventory inventory, string trigger)
    {
        if (!telemetry.IsEnabled) return;
        telemetry.Track(TelemetrySchema.Events.SettingsSnapshot, Build(settings, inventory, trigger));
    }

    public static Dictionary<string, object?> Build(ISettingsService settings, Inventory inventory, string trigger)
    {
        var browser = settings.GetBrowserConfig();
        var kafka = settings.GetKafkaConfig();
        var defaultBrowser = new BrowserConfig();
        var defaultKafka = new KafkaConfig();

        var theme = settings.GetValue("Theme") ?? DefaultTheme;
        var autoCheck = !string.Equals(settings.GetValue("AutoCheckForUpdates"), "false", StringComparison.OrdinalIgnoreCase);
        var connectionCheck = int.TryParse(settings.GetValue(PreferencesViewModel.CONNECTION_CHECK_INTERVAL_SECONDS_KEY),
            out var cc) ? cc : DefaultConnectionCheckSeconds;

        var kafkaTimeoutsDefault =
            kafka.QueryWatermarkTimeoutMs == defaultKafka.QueryWatermarkTimeoutMs &&
            kafka.QueryTopicsTimeoutMs == defaultKafka.QueryTopicsTimeoutMs &&
            kafka.ConsumeTimeoutMs == defaultKafka.ConsumeTimeoutMs &&
            kafka.AdminMetadataTimeoutMs == defaultKafka.AdminMetadataTimeoutMs;

        return new Dictionary<string, object?>
        {
            ["trigger"] = trigger,
            ["theme"] = IsBuiltInTheme(theme) ? theme : "plugin",
            ["theme_is_default"] = theme == DefaultTheme,
            ["font_size"] = browser.FontSize,
            ["font_size_is_default"] = browser.FontSize == defaultBrowser.FontSize,
            ["default_fetch_count"] = browser.DefaultFetchCount,
            ["default_fetch_count_is_default"] = browser.DefaultFetchCount == defaultBrowser.DefaultFetchCount,
            ["fetch_counts_is_default"] = browser.FetchCounts.SetEquals(defaultBrowser.FetchCounts),
            ["restore_tabs_on_startup"] = browser.RestoreTabsOnStartup,
            ["restore_tabs_on_startup_is_default"] = browser.RestoreTabsOnStartup == defaultBrowser.RestoreTabsOnStartup,
            ["eager_load_topics"] = browser.EagerLoadTopicsOnStartup,
            ["eager_load_topics_is_default"] = browser.EagerLoadTopicsOnStartup == defaultBrowser.EagerLoadTopicsOnStartup,
            ["show_save_notification"] = browser.ShowSaveNotification,
            ["show_save_notification_is_default"] = browser.ShowSaveNotification == defaultBrowser.ShowSaveNotification,
            ["saved_dir_is_default"] = string.Equals(browser.SavedMessagesDirectory, BrowserConfig.DefaultSavedMessagesDirectory,
                StringComparison.Ordinal),
            ["auto_check_updates"] = autoCheck,
            ["auto_check_updates_is_default"] = autoCheck,
            ["connection_check_interval_s"] = connectionCheck,
            ["connection_check_interval_s_is_default"] = connectionCheck == DefaultConnectionCheckSeconds,
            ["hidden_key_formatters"] = CountList(settings.GetValue("HiddenKeyFormatters")),
            ["hidden_value_formatters"] = CountList(settings.GetValue("HiddenValueFormatters")),
            ["kafka_timeouts_is_default"] = kafkaTimeoutsDefault,
            ["fetch_max_bytes"] = kafka.FetchMaxBytes,
            ["fetch_max_bytes_is_default"] = kafka.FetchMaxBytes == defaultKafka.FetchMaxBytes,
            ["clusters"] = TelemetryBuckets.Count(inventory.Clusters),
            ["clients_local"] = inventory.LocalClients,
            ["clients_grpc"] = inventory.GrpcClients,
            ["clients_rest"] = inventory.RestClients,
            ["plugins_installed"] = inventory.PluginsInstalled,
            ["plugins_enabled"] = inventory.PluginsEnabled,
        };
    }

    private static bool IsBuiltInTheme(string theme) => theme is "System" or "Light" or "Dark";

    private static int CountList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value == "[]") return 0;
        return value.Trim('[', ']').Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Count(v => v.Trim('"').Length > 0);
    }
}
