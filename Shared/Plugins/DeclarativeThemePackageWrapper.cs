using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Serilog;

namespace KafkaLens.Shared.Plugins;

/// <summary>
/// Simple wrapper for declarative theme packages that can be used without Avalonia dependencies.
/// This is used by PluginRegistry to register theme packages, while the actual implementation
/// is in AvaloniaApp.Plugins.DeclarativeThemePackage.
/// </summary>
public class DeclarativeThemePackageWrapper : IThemePackage
{
    private readonly string _pluginPath;
    private readonly ThemeManifest _manifest;

    public string PackageName => _manifest.PackageName;
    public string Author => _manifest.Author;
    public string Description => _manifest.Description;

    public DeclarativeThemePackageWrapper(string pluginPath)
    {
        _pluginPath = pluginPath;
        _manifest = LoadManifest();
    }

    private ThemeManifest LoadManifest()
    {
        var manifestPath = Path.Combine(_pluginPath, "themes.json");
        if (!File.Exists(manifestPath))
        {
            throw new InvalidOperationException($"themes.json not found in {_pluginPath}");
        }

        try
        {
            var json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<ThemeManifest>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            return manifest ?? throw new InvalidOperationException("Failed to parse themes.json");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load themes.json from {Path}", manifestPath);
            throw;
        }
    }

    public IReadOnlyList<IThemeInfo> GetThemes()
    {
        var themes = new List<IThemeInfo>();
        
        foreach (var themeConfig in _manifest.Themes)
        {
            themes.Add(new DeclarativeThemeInfo(_pluginPath, themeConfig));
        }

        return themes;
    }
}

/// <summary>
/// Simple wrapper for theme info that can be used without Avalonia dependencies.
/// </summary>
public class DeclarativeThemeInfo : IThemeInfo
{
    private readonly string _pluginPath;
    private readonly ThemeConfig _config;

    public string Id => _config.Id;
    public string DisplayName => _config.DisplayName;
    public string Description => _config.Description ?? "";
    public object BaseVariant => null; // Will be set by ThemeService
    public object PluginTheme => null;  // Will be set by ThemeService

    public DeclarativeThemeInfo(string pluginPath, ThemeConfig config)
    {
        _pluginPath = pluginPath;
        _config = config;
    }

    public bool ValidateTheme()
    {
        // Check if the theme file exists
        var themePath = Path.Combine(_pluginPath, _config.ResourceFile);
        return File.Exists(themePath);
    }
}

/// <summary>
/// Theme manifest configuration (themes.json).
/// </summary>
public class ThemeManifest
{
    public string PackageName { get; set; } = "";
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ThemeConfig> Themes { get; set; } = new();
}

/// <summary>
/// Individual theme configuration.
/// </summary>
public class ThemeConfig
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Description { get; set; }
    public string BaseVariant { get; set; } = "Light";
    public string ResourceFile { get; set; } = "";
}
