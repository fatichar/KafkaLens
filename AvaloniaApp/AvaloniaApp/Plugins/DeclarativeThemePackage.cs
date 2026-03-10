using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Plugins;
using KafkaLens.Shared.Plugins;
using Serilog;

namespace AvaloniaApp.Plugins;

/// <summary>
/// Declarative theme package that loads themes from JSON manifest without requiring C# classes.
/// Themes are defined purely in configuration files.
/// </summary>
[KafkaLensExtension(typeof(KafkaLens.Shared.Plugins.IThemePackage))]
public class DeclarativeThemePackage : KafkaLens.Shared.Plugins.IThemePackage
{
    private readonly string _pluginPath;
    private readonly ThemeManifest _manifest;
    private readonly List<DeclarativeTheme> _themes;

    public string PackageName => _manifest.PackageName;
    public string Author => _manifest.Author;
    public string Description => _manifest.Description;

    public DeclarativeThemePackage(string pluginPath)
    {
        _pluginPath = pluginPath;
        _manifest = LoadManifest();
        _themes = LoadThemes();
    }

    public IReadOnlyList<KafkaLens.Shared.Plugins.IThemeInfo> GetThemes() => _themes.Cast<KafkaLens.Shared.Plugins.IThemeInfo>().ToList();

    private ThemeManifest LoadManifest()
    {
        var manifestPath = Path.Combine(_pluginPath, "themes.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Theme manifest not found: {manifestPath}");
        }

        var json = File.ReadAllText(manifestPath);
        var manifest = JsonSerializer.Deserialize<ThemeManifest>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        return manifest ?? throw new InvalidOperationException("Failed to parse theme manifest");
    }

    private List<DeclarativeTheme> LoadThemes()
    {
        var themes = new List<DeclarativeTheme>();
        
        foreach (var themeConfig in _manifest.Themes)
        {
            themes.Add(new DeclarativeTheme(themeConfig, _pluginPath));
        }

        return themes;
    }
}

/// <summary>
/// Individual theme loaded from declarative configuration.
/// </summary>
public class DeclarativeTheme : IThemeInfo
{
    private readonly ThemeConfig _config;
    private readonly string _pluginPath;

    public string Id => _config.Id;
    public string DisplayName => _config.DisplayName;
    public string Description => _config.Description ?? "";
    public object BaseVariant => ParseThemeVariant(_config.BaseVariant);

    public DeclarativeTheme(ThemeConfig config, string pluginPath)
    {
        _config = config;
        _pluginPath = pluginPath;
    }

    public ResourceDictionary? LoadThemeResources()
    {
        try
        {
            var themePath = Path.Combine(_pluginPath, _config.ResourceFile);
            if (!File.Exists(themePath))
            {
                Log.Warning("Theme resource file not found: {Path}", themePath);
                return null;
            }

            // Load XAML from file (not embedded resource)
            var xaml = File.ReadAllText(themePath);
            var resourceDict = (ResourceDictionary)Avalonia.Markup.Xaml.AvaloniaRuntimeXamlLoader.Load(xaml);
            Log.Information("Successfully loaded theme resources for {ThemeId} from {Path}", _config.Id, themePath);
            return resourceDict;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load theme resources for {ThemeId}", _config.Id);
            return null;
        }
    }

    public bool ValidateTheme()
    {
        try
        {
            var resources = LoadThemeResources();
            return resources != null && resources.Count > 0;
        }
        catch
        {
            return false;
        }
    }

    private static ThemeVariant ParseThemeVariant(string variant)
    {
        return variant.ToLowerInvariant() switch
        {
            "dark" => ThemeVariant.Dark,
            "light" => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
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
