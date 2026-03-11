using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Styling;
using Avalonia.Controls;
using AvaloniaApp.Plugins;
using KafkaLens.Shared.Plugins;
using KafkaLens.Shared.Services;
using Serilog;

namespace AvaloniaApp.Services;

/// <summary>
/// Manages theme discovery, loading, and application for both built-in and plugin themes.
/// Built-in themes are always available for reliability. Plugin themes are optional extensions.
/// </summary>
public class ThemeService : IThemeService
{
    private readonly ExtensionRegistry _extensionRegistry;
    private readonly string _pluginsDir;
    private readonly Dictionary<string, ThemeInfo> _availableThemes = new();
    private readonly string[] _builtInThemes = { "Light", "Dark", "System" };

    public ThemeService(ExtensionRegistry extensionRegistry, string pluginsDir)
    {
        _extensionRegistry = extensionRegistry;
        _pluginsDir        = pluginsDir;
        InitializeThemes();
    }

    /// <summary>
    /// Gets all available themes (built-in + plugin themes).
    /// </summary>
    public IReadOnlyList<ThemeInfo> GetAvailableThemes() => _availableThemes.Values.ToList();

    /// <summary>
    /// Gets a specific theme by ID (case-insensitive).
    /// </summary>
    public ThemeInfo? GetTheme(string id)
    {
        if (_availableThemes.TryGetValue(id, out var theme))
            return theme;

        // Case-insensitive fallback
        var match = _availableThemes.FirstOrDefault(
            kvp => string.Equals(kvp.Key, id, StringComparison.OrdinalIgnoreCase));
        return match.Value ?? null;
    }

    /// <summary>
    /// Loads theme resources for a given theme ID.
    /// Returns null if theme not found or loading fails.
    /// </summary>
    public object? LoadThemeResources(string themeId)
    {
        Log.Information("LoadThemeResources called with themeId: {ThemeId}", themeId);

        var theme = GetTheme(themeId);
        if (theme == null)
        {
            Log.Warning("Theme {ThemeId} not found in available themes", themeId);
            return null;
        }

        return theme.IsBuiltIn
            ? LoadBuiltInThemeResources(theme.Id)
            : LoadPluginThemeResources(theme);
    }

    private void InitializeThemes()
    {
        Log.Information("Initializing themes...");

        // Register built-in themes
        foreach (var themeName in _builtInThemes)
        {
            _availableThemes[themeName] = new ThemeInfo
            {
                Id          = themeName,
                DisplayName = themeName,
                IsBuiltIn   = true,
                // "System" has no fixed base variant; the platform decides.
                BaseVariant = BuiltInThemeBase(themeName)
            };
            Log.Information("Registered built-in theme: {ThemeName}", themeName);
        }

        RegisterPluginThemes();

        Log.Information("Theme initialization complete. Total themes: {Count}", _availableThemes.Count);
    }

    private void RegisterPluginThemes()
    {
        Log.Information("RegisterPluginThemes called");

        // Register multi-theme packages (preferred approach)
        var themePackages = _extensionRegistry.GetExtensions<KafkaLens.Shared.Plugins.IThemePackage>();
        Log.Information("Found {Count} theme packages", themePackages.Count);

        foreach (var package in themePackages)
        {
            try
            {
                Log.Information("Processing theme package: {PackageName}", package.PackageName);
                var themes = package.GetThemes();

                foreach (var theme in themes)
                {
                    if (!theme.ValidateTheme())
                    {
                        Log.Warning("Theme {ThemeId} from package {Package} failed validation",
                            theme.Id, package.PackageName);
                        continue;
                    }

                    _availableThemes[theme.Id] = new ThemeInfo
                    {
                        Id          = theme.Id,
                        DisplayName = theme.DisplayName,
                        Author      = package.Author,
                        Description = theme.Description,
                        IsBuiltIn   = false,
                        BaseVariant = theme.BaseVariant,
                        PluginTheme = theme
                    };
                    Log.Information("Registered theme from package {Package}: {ThemeId} - {DisplayName}",
                        package.PackageName, theme.Id, theme.DisplayName);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to register themes from package {Package}", package.PackageName);
            }
        }

        // Register legacy single-theme plugins (backward compatibility)
        var legacyThemes = _extensionRegistry.GetExtensions<AvaloniaApp.Plugins.ITheme>();
        foreach (var theme in legacyThemes)
        {
            try
            {
                if (!theme.ValidateTheme())
                {
                    Log.Warning("Legacy theme {ThemeId} failed validation", theme.Id);
                    continue;
                }

                _availableThemes[theme.Id] = new ThemeInfo
                {
                    Id          = theme.Id,
                    DisplayName = theme.DisplayName,
                    Author      = theme.Author,
                    Description = theme.Description,
                    IsBuiltIn   = false,
                    BaseVariant = theme.BaseVariant,
                    PluginTheme = theme
                };
                Log.Information("Registered legacy theme: {ThemeId} - {DisplayName}", theme.Id, theme.DisplayName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to register legacy theme {ThemeId}", theme.Id);
            }
        }
    }

    /// <summary>
    /// Maps a <see cref="ThemeBase"/> value to the Avalonia <see cref="ThemeVariant"/>.
    /// </summary>
    public static ThemeVariant ThemeBaseToVariant(ThemeBase? themeBase) =>
        themeBase switch
        {
            ThemeBase.Dark  => ThemeVariant.Dark,
            ThemeBase.Light => ThemeVariant.Light,
            null            => ThemeVariant.Default,
            _               => ThemeVariant.Default   // future enum values default to system variant
        };

    /// <summary>
    /// Returns the <see cref="ThemeBase"/> for well-known built-in theme names,
    /// or <c>null</c> for "System" (the platform decides the variant at runtime).
    /// </summary>
    private static ThemeBase? BuiltInThemeBase(string themeName) =>
        themeName switch
        {
            "Dark" or "Gray" => ThemeBase.Dark,
            "Light"          => ThemeBase.Light,
            _                => null
        };

    private ResourceDictionary? LoadBuiltInThemeResources(string themeName)
    {
        var uri = $"avares://AvaloniaApp/Themes/{themeName}.axaml";
        Log.Information("Loading built-in theme {ThemeName} from {Uri}", themeName, uri);

        try
        {
            var resourceDict = (ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(new Uri(uri));
            Log.Information("Successfully loaded built-in theme {ThemeName}", themeName);
            return resourceDict;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load built-in theme {ThemeName} from {Uri}", themeName, uri);

            // Fallback to external file
            var externalPath = Path.Combine(AppContext.BaseDirectory, "Themes", $"{themeName}.axaml");
            if (File.Exists(externalPath))
            {
                try
                {
                    var resourceDict = (ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(new Uri(externalPath));
                    Log.Information("Successfully loaded external built-in theme {ThemeName}", themeName);
                    return resourceDict;
                }
                catch (Exception ex2)
                {
                    Log.Warning(ex2, "Could not load external built-in theme {ThemeName} from {Path}",
                        themeName, externalPath);
                }
            }
        }

        Log.Error("Failed to load built-in theme {ThemeName} from any source", themeName);
        return null;
    }

    private ResourceDictionary? LoadPluginThemeResources(ThemeInfo themeInfo)
    {
        if (themeInfo.PluginTheme == null)
        {
            Log.Error("Plugin theme {ThemeId} has no plugin instance", themeInfo.Id);
            return null;
        }

        try
        {
            // Avalonia-native IThemeInfo — use LoadThemeResources directly
            if (themeInfo.PluginTheme is AvaloniaApp.Plugins.IThemeInfo avaloniaTheme)
            {
                var resources = avaloniaTheme.LoadThemeResources();
                if (resources != null)
                {
                    Log.Information("Successfully loaded Avalonia plugin theme {ThemeId}", themeInfo.Id);
                    return resources;
                }

                Log.Warning("Avalonia plugin theme {ThemeId} returned null resources", themeInfo.Id);
                return null;
            }

            // Shared IThemeInfo (e.g. declarative package registered before Avalonia loaded it).
            // Scan the injected plugins directory for the matching themes.json.
            if (themeInfo.PluginTheme is KafkaLens.Shared.Plugins.IThemeInfo sharedTheme)
            {
                foreach (var pluginDir in Directory.GetDirectories(_pluginsDir))
                {
                    if (!File.Exists(Path.Combine(pluginDir, "themes.json"))) continue;

                    try
                    {
                        var package              = new DeclarativeThemePackage(pluginDir);
                        var avaloniaThemeInstance = package.GetThemes()
                            .OfType<AvaloniaApp.Plugins.IThemeInfo>()
                            .FirstOrDefault(t => t.Id == sharedTheme.Id);

                        if (avaloniaThemeInstance != null)
                        {
                            var resources = avaloniaThemeInstance.LoadThemeResources();
                            if (resources != null)
                            {
                                Log.Information("Loaded shared plugin theme {ThemeId} from {Dir}",
                                    themeInfo.Id, pluginDir);
                                return resources;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to load theme {ThemeId} from {Dir}", themeInfo.Id, pluginDir);
                    }
                }
            }

            Log.Error("Plugin theme {ThemeId} could not be loaded — unsupported type", themeInfo.Id);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load plugin theme {ThemeId}", themeInfo.Id);
            return null;
        }
    }

    /// <inheritdoc/>
    public string GetDefaultTheme() => "Light";
}
