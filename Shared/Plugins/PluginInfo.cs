namespace KafkaLens.Shared.Plugins;

public sealed class PluginInfo
{
    public string  Id          { get; init; } = "";
    public string  Name        { get; init; } = "";
    public string  Version     { get; init; } = "";
    public string  Author      { get; init; } = "";
    public string  Description { get; init; } = "";

    /// <summary>Optional category tag from plugin.json (e.g. "theme", "formatter", "auth").</summary>
    public string  Category    { get; init; } = "";

    /// <summary>Path to the plugin DLL.</summary>
    public string  FilePath    { get; init; } = "";

    /// <summary>
    /// Path to the plugin's containing folder (for folder-based plugins).
    /// Empty for legacy flat-DLL plugins.
    /// </summary>
    public string  FolderPath  { get; init; } = "";

    /// <summary>Path to icon.png inside the plugin folder, or empty if none.</summary>
    public string  IconPath    { get; init; } = "";

    /// <summary>Optional homepage URL from plugin.json or repository metadata.</summary>
    public string? Homepage    { get; init; }

    public bool    IsEnabled   { get; set;  } = true;
}
