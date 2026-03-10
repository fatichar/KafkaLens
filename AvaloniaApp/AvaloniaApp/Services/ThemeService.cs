using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Styling;
using Avalonia.Controls;
using AvaloniaApp.Plugins;
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
    private readonly Dictionary<string, ThemeInfo> _availableThemes = new();
    private readonly string[] _builtInThemes = { "Light", "Dark", "System" };

    public ThemeService(ExtensionRegistry extensionRegistry)
    {
        _extensionRegistry = extensionRegistry;
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
        // Try exact match first
        if (_availableThemes.TryGetValue(id, out var theme))
            return theme;
            
        // Try case-insensitive match
        var caseInsensitiveMatch = _availableThemes.FirstOrDefault(kvp => 
            string.Equals(kvp.Key, id, StringComparison.OrdinalIgnoreCase));
        return caseInsensitiveMatch.Value ?? null;
    }

    /// <summary>
    /// Loads theme resources for a given theme ID.
    /// Returns null if theme not found or loading fails.
    /// </summary>
    public object? LoadThemeResources(string themeId)
    {
        Log.Information("LoadThemeResources called with themeId: {ThemeId}", themeId);
        Log.Information("Available themes: {Themes}", string.Join(", ", _availableThemes.Keys));
        
        var theme = GetTheme(themeId);
        if (theme == null)
        {
            Log.Warning("Theme {ThemeId} not found in available themes", themeId);
            return null;
        }

        Log.Information("Found theme {ThemeId}, IsBuiltIn: {IsBuiltIn}", theme.Id, theme.IsBuiltIn);

        if (theme.IsBuiltIn)
        {
            return LoadBuiltInThemeResources(theme.Id);
        }
        else
        {
            return LoadPluginThemeResources(theme);
        }
    }

    private void InitializeThemes()
    {
        Log.Information("Initializing themes...");
        
        // Register built-in themes
        foreach (var themeName in _builtInThemes)
        {
            _availableThemes[themeName] = new ThemeInfo
            {
                Id = themeName,
                DisplayName = themeName,
                IsBuiltIn = true,
                BaseVariant = (object?)GetBuiltInThemeVariant(themeName)
            };
            Log.Information("Registered built-in theme: {ThemeName}", themeName);
        }

        // Register plugin themes from both ITheme and IThemePackage plugins
        RegisterPluginThemes();
        
        Log.Information("Theme initialization complete. Total themes: {Count}", _availableThemes.Count);
    }

    private void RegisterPluginThemes()
    {
        Log.Information("RegisterPluginThemes called");
        
        // Register multi-theme packages (preferred approach)
        var themePackages = _extensionRegistry.GetExtensions<KafkaLens.Shared.Plugins.IThemePackage>();
        Log.Information("Found {Count} theme packages", themePackages.Count());
        
        foreach (var package in themePackages)
        {
            try
            {
                Log.Information("Processing theme package: {PackageName}", package.PackageName);
                var themes = package.GetThemes();
                Log.Information("Found {Count} themes in package {PackageName}", themes.Count, package.PackageName);
                
                foreach (var theme in themes)
                {
                    if (theme.ValidateTheme())
                    {
                        _availableThemes[theme.Id] = new ThemeInfo
                        {
                            Id = theme.Id,
                            DisplayName = theme.DisplayName,
                            Author = package.Author, // Use package author for all themes
                            Description = theme.Description,
                            IsBuiltIn = false,
                            BaseVariant = theme.BaseVariant,
                            PluginTheme = theme
                        };
                        Log.Information("Registered theme from package {Package}: {ThemeId} - {DisplayName}", 
                            package.PackageName, theme.Id, theme.DisplayName);
                    }
                    else
                    {
                        Log.Warning("Theme {ThemeId} from package {Package} failed validation", theme.Id, package.PackageName);
                    }
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
                if (theme.ValidateTheme())
                {
                    _availableThemes[theme.Id] = new ThemeInfo
                    {
                        Id = theme.Id,
                        DisplayName = theme.DisplayName,
                        Author = theme.Author,
                        Description = theme.Description,
                        IsBuiltIn = false,
                        BaseVariant = (object?)theme.BaseVariant,
                        PluginTheme = theme
                    };
                    Log.Information("Registered legacy theme: {ThemeId} - {DisplayName}", theme.Id, theme.DisplayName);
                }
                else
                {
                    Log.Warning("Legacy theme {ThemeId} failed validation", theme.Id);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to register legacy theme {ThemeId}", theme.Id);
            }
        }
    }

    private static ThemeVariant GetBuiltInThemeVariant(string themeName)
    {
        return themeName switch
        {
            "System" => ThemeVariant.Default,
            "Dark" or "Gray" => ThemeVariant.Dark,
            _ => ThemeVariant.Light
        };
    }

    private ResourceDictionary? LoadBuiltInThemeResources(string themeName)
    {
        // Try embedded resource first (built-in themes)
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
            
            // Try external file as fallback
            var externalPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Themes", $"{themeName}.axaml");
            if (System.IO.File.Exists(externalPath))
            {
                try
                {
                    var resourceDict = (ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaXamlLoader.Load(new Uri(externalPath));
                    Log.Information("Successfully loaded external built-in theme {ThemeName}", themeName);
                    return resourceDict;
                }
                catch (Exception ex2)
                {
                    Log.Warning(ex2, "Could not load external built-in theme {ThemeName} from {Path}", themeName, externalPath);
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
            // Check if it's an Avalonia theme with LoadThemeResources method
            if (themeInfo.PluginTheme is AvaloniaApp.Plugins.IThemeInfo avaloniaTheme)
            {
                var resources = avaloniaTheme.LoadThemeResources();
                if (resources != null)
                {
                    Log.Information("Successfully loaded Avalonia plugin theme {ThemeId}", themeInfo.Id);
                    return resources;
                }
                else
                {
                    Log.Warning("Avalonia plugin theme {ThemeId} returned null resources", themeInfo.Id);
                    return null;
                }
            }
            
            // For Shared IThemeInfo, we need to create a DeclarativeTheme to load resources
            if (themeInfo.PluginTheme is KafkaLens.Shared.Plugins.IThemeInfo sharedTheme)
            {
                // Try to find the plugin directory and create an Avalonia DeclarativeTheme
                var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var pluginsDir = Path.Combine(appDataPath, "KafkaLens", "plugins");
                
                // Look for a plugin directory that contains this theme
                foreach (var pluginDir in Directory.GetDirectories(pluginsDir))
                {
                    var themesJsonPath = Path.Combine(pluginDir, "themes.json");
                    if (File.Exists(themesJsonPath))
                    {
                        try
                        {
                            var package = new DeclarativeThemePackage(pluginDir);
                            var themes = package.GetThemes();
                            var avaloniaThemeInstance = themes.FirstOrDefault(t => t.Id == sharedTheme.Id);
                            
                            if (avaloniaThemeInstance != null && avaloniaThemeInstance is AvaloniaApp.Plugins.IThemeInfo avaloniaThemeWithResources)
                            {
                                var resources = avaloniaThemeWithResources.LoadThemeResources();
                                if (resources != null)
                                {
                                    Log.Information("Successfully loaded shared plugin theme {ThemeId} from {Dir}", themeInfo.Id, pluginDir);
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
            }
            
            Log.Error("Plugin theme {ThemeId} could not be loaded - unsupported type", themeInfo.Id);
            return null;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load plugin theme {ThemeId}", themeInfo.Id);
            return null;
        }
    }

    /// <summary>
    /// Gets the default theme to fall back to if a theme fails to load.
    /// </summary>
    public string GetDefaultTheme() => "Light";
}
