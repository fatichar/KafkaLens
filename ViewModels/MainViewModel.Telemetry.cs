using KafkaLens.ViewModels.Telemetry;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel
{
    internal const string TelemetryNoticeTitle = "Help improve KafkaLens";

    /// <summary>
    /// UI callback (set by the view layer): shows the first-run usage-data notice and returns
    /// true to keep telemetry enabled, false to disable it, null if it could not be shown.
    /// </summary>
    public static Func<string, string, Task<bool?>> ConfirmTelemetryNotice { get; set; } = (_, _) => Task.FromResult<bool?>(null);

    /// <summary>Shows the first-run usage-data notice once, if telemetry needs it.</summary>
    public async Task ShowTelemetryNoticeIfNeededAsync()
    {
        if (telemetry is not ITelemetryControl control || !control.NeedsNotice) return;

        try
        {
            var message =
                "KafkaLens sends anonymous usage data (app version, OS, which features are used, " +
                "fetch sizes and timings, settings, and crash details) to help decide what to improve.\n\n" +
                "It never sends cluster, broker, topic or message data, file paths, or anything that identifies you.\n\n" +
                $"Details: {control.PrivacyUrl}\n" +
                "You can change this any time in Preferences → Privacy.";
            var keepEnabled = await ConfirmTelemetryNotice(TelemetryNoticeTitle, message);
            // Not shown (no window): ask again next launch; nothing is sent meanwhile.
            if (keepEnabled is { } choice) control.AcknowledgeNotice(choice);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to show telemetry notice");
        }
    }

    private void TrackTabShortcut() => telemetry.Feature(TelemetrySchema.Features.TabSwitchShortcut, "shortcut");

    private void SendSettingsSnapshot(string trigger)
    {
        try
        {
            SettingsSnapshot.Send(telemetry, settingsService, BuildTelemetryInventory(), trigger);
        }
        catch (Exception e)
        {
            Log.Debug(e, "Failed to send settings snapshot");
        }
    }

    private void SendSettingsSnapshotIfDue()
    {
        try
        {
            SettingsSnapshot.SendIfDue(telemetry, settingsService, BuildTelemetryInventory());
        }
        catch (Exception e)
        {
            Log.Debug(e, "Failed to send settings snapshot");
        }
    }

    internal SettingsSnapshot.Inventory BuildTelemetryInventory()
    {
        var clients = ClientInfoRepository.GetAll()?.Values.ToList() ?? new();
        var pluginStates = settingsService.GetPluginSettings().PluginStates;
        return new SettingsSnapshot.Inventory(
            Clusters: Clusters.Count(c => TelemetryExtensions.ClientKind(c.Client) != "saved"),
            LocalClients: Clusters.Count(c => TelemetryExtensions.ClientKind(c.Client) == "local"),
            GrpcClients: clients.Count(c => string.Equals(c.Protocol, "grpc", StringComparison.OrdinalIgnoreCase)),
            RestClients: clients.Count(c => string.Equals(c.Protocol, "rest", StringComparison.OrdinalIgnoreCase)),
            PluginsInstalled: pluginStates.Count,
            PluginsEnabled: pluginStates.Count(p => p.Value));
    }
}
