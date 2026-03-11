using System.Collections.Generic;
using KafkaLens.Shared.Plugins;

namespace KafkaLens.Shared.Services;

/// <summary>
/// Service for managing theme discovery and loading.
/// Interface kept in Shared to avoid UI dependencies.
/// </summary>
public interface IThemeService
{
    /// <summary>
    /// Gets all available themes.
    /// </summary>
    IReadOnlyList<ThemeInfo> GetAvailableThemes();

    /// <summary>
    /// Gets theme information by ID.
    /// </summary>
    ThemeInfo? GetTheme(string themeId);

    /// <summary>
    /// Gets the default theme to fall back to if a theme fails to load.
    /// </summary>
    string GetDefaultTheme();

    /// <summary>
    /// Loads theme resources for the specified theme.
    /// Returns null if theme not found or loading fails.
    /// </summary>
    object? LoadThemeResources(string themeId);
}

/// <summary>
/// Information about an available theme.
/// </summary>
public class ThemeInfo
{
    public string Id          { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Author      { get; set; } = "";
    public string Description { get; set; } = "";
    public bool   IsBuiltIn   { get; set; }

    /// <summary>
    /// Base variant (Light / Dark) used by the UI layer to select the underlying palette.
    /// <c>null</c> for built-in themes that handle variant selection themselves (e.g. "System").
    /// </summary>
    public ThemeBase? BaseVariant { get; set; }

    /// <summary>
    /// Opaque reference to the underlying plugin theme object.
    /// The UI layer downcasts this to its own <c>IThemeInfo</c> to load resources.
    /// </summary>
    public object? PluginTheme { get; set; }
}
