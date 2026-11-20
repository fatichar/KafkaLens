namespace KafkaLens.ViewModels.Telemetry;

/// <summary>
/// The complete, allow-listed telemetry schema. Any property not listed here is dropped
/// before sending. Keep docs/privacy.html in sync (a unit test enumerates this schema).
/// </summary>
public static class TelemetrySchema
{
    public const int Version = 2;

    public static class Events
    {
        public const string AppFirstRun = "app_first_run";
        public const string AppStarted = "app_started";
        public const string Heartbeat = "heartbeat";
        public const string AppExited = "app_exited";
        public const string AppError = "app_error";
        public const string ClusterConnected = "cluster_connected";
        public const string TopicsAvailable = "topics_available";
        public const string FetchCompleted = "fetch_completed";
        public const string FeatureUsed = "feature_used";
        public const string FormatterSelected = "formatter_selected";
        public const string UpdateCheck = "update_check";
        public const string UpdateStarted = "update_started";
        public const string PluginInstalled = "plugin_installed";
        public const string PluginRemoved = "plugin_removed";
        public const string SettingsSnapshot = "settings_snapshot";
    }

    public static class Features
    {
        public const string SaveMessages = "save_messages";
        public const string OpenSavedMessages = "open_saved_messages";
        public const string CopyKey = "copy_key";
        public const string CopyValue = "copy_value";
        public const string CopyOffset = "copy_offset";
        public const string CopyTimestamp = "copy_timestamp";
        public const string CopyAll = "copy_all";
        public const string PositiveFilter = "positive_filter";
        public const string NegativeFilter = "negative_filter";
        public const string LineFilter = "line_filter";
        public const string TopicFilter = "topic_filter";
        public const string SortMessages = "sort_messages";
        public const string NavigatorToggle = "navigator_toggle";
        public const string FetchPanelToggle = "fetch_panel_toggle";
        public const string StopFetch = "stop_fetch";
        public const string RefreshFetch = "refresh_fetch";
        public const string TabOpen = "tab_open";
        public const string TabClose = "tab_close";
        public const string TabSwitchShortcut = "tab_switch_shortcut";
        public const string EditClusters = "edit_clusters";
        public const string PreferencesOpen = "preferences_open";
        public const string PreferencesSaved = "preferences_saved";
        public const string FormatterPreferencesOpen = "formatter_preferences_open";
        public const string PluginManagerOpen = "plugin_manager_open";
        public const string ThemeChanged = "theme_changed";
        public const string AppLogToggle = "app_log_toggle";
        public const string DiagnosticLogOpen = "diagnostic_log_open";
        public const string ApplyFormatterAllClusters = "apply_formatter_all_clusters";
        public const string TopicSettingsSaved = "topic_settings_saved";
        public const string GuessFormatter = "guess_formatter";
        public const string ConnectRetry = "connect_retry";
        public const string RestoreTabs = "restore_tabs";
        public const string AboutOpen = "about_open";
        public const string UpdateCheckManual = "update_check_manual";
        public const string AutoUpdateToggle = "auto_update_toggle";
    }

    /// <summary>Properties added by the service to every event.</summary>
    public static readonly IReadOnlySet<string> CommonProperties = new HashSet<string>
    {
        "app_version", "os", "os_version", "arch", "runtime", "install_kind", "locale", "session_id", "seq", "launch_elapsed_ms", "schema"
    };

    /// <summary>Per-event allow-list of properties.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlySet<string>> EventProperties =
        new Dictionary<string, IReadOnlySet<string>>
        {
            [Events.AppFirstRun] = Set(),
            [Events.AppStarted] = Set("duration_ms", "previous_unclean_exit", "plugins_loaded"),
            [Events.Heartbeat] = Set("active_s"),
            [Events.AppExited] = Set("session_active_s", "session_wall_s"),
            [Events.AppError] = Set("kind", "exception_type", "inner_type", "frames", "fingerprint"),
            [Events.ClusterConnected] = Set("client_kind", "result", "topics", "duration_ms", "attempts"),
            [Events.TopicsAvailable] = Set("source"),
            [Events.FetchCompleted] = Set("client_kind", "node", "position", "direction", "requested",
                "received", "duration_ms", "result"),
            [Events.FeatureUsed] = Set("feature", "source"),
            [Events.FormatterSelected] = Set("formatter", "target", "auto"),
            [Events.UpdateCheck] = Set("result", "manual"),
            [Events.UpdateStarted] = Set("to_version"),
            [Events.PluginInstalled] = Set("plugin_id"),
            [Events.PluginRemoved] = Set(),
            [Events.SettingsSnapshot] = Set(
                "theme", "theme_is_default",
                "font_size", "font_size_is_default",
                "default_fetch_count", "default_fetch_count_is_default",
                "fetch_counts_is_default",
                "restore_tabs_on_startup", "restore_tabs_on_startup_is_default",
                "eager_load_topics", "eager_load_topics_is_default",
                "show_save_notification", "show_save_notification_is_default",
                "saved_dir_is_default",
                "auto_check_updates", "auto_check_updates_is_default",
                "connection_check_interval_s", "connection_check_interval_s_is_default",
                "hidden_key_formatters", "hidden_value_formatters",
                "kafka_timeouts_is_default", "fetch_max_bytes", "fetch_max_bytes_is_default",
                "clusters", "clients_local", "clients_grpc", "clients_rest",
                "plugins_installed", "plugins_enabled",
                "trigger"),
        };

    private static IReadOnlySet<string> Set(params string[] names) => new HashSet<string>(names);
}
