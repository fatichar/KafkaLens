using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Serilog;

namespace KafkaLens.Shared.Plugins;

/// <summary>
/// Shared-layer wrapper for declarative theme packages (JSON-only, no C# required).
/// Parses <c>themes.json</c> and exposes the package as an <see cref="IThemePackage"/>
/// without any Avalonia dependency.  The Avalonia layer uses this wrapper to register
/// the package and then loads XAML resources via its own <c>DeclarativeThemePackage</c>.
/// </summary>
public class DeclarativeThemePackageWrapper : IThemePackage
{
    private readonly string _pluginPath;
    private readonly ThemeManifest _manifest;

    public string PackageName => _manifest.PackageName;
    public string Author      => _manifest.Author;
    public string Description => _manifest.Description;

    public DeclarativeThemePackageWrapper(string pluginPath)
    {
        _pluginPath = pluginPath;
        _manifest   = LoadManifest();
    }

    private ThemeManifest LoadManifest()
    {
        var manifestPath = Path.Combine(_pluginPath, "themes.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException($"themes.json not found in {_pluginPath}");

        try
        {
            var json     = File.ReadAllText(manifestPath);
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
        foreach (var config in _manifest.Themes)
            themes.Add(new DeclarativeThemeInfo(_pluginPath, config));
        return themes;
    }
}

/// <summary>
/// Shared-layer theme info for a single declarative theme entry.
/// <see cref="ValidateTheme"/> checks that the XAML resource file exists on disk.
/// </summary>
public class DeclarativeThemeInfo : IThemeInfo
{
    private readonly string _pluginPath;
    private readonly ThemeConfig _config;

    public string    Id          => _config.Id;
    public string    DisplayName => _config.DisplayName;
    public string    Description => _config.Description ?? "";
    public ThemeBase BaseVariant => ParseThemeBase(_config.BaseVariant);

    public DeclarativeThemeInfo(string pluginPath, ThemeConfig config)
    {
        _pluginPath = pluginPath;
        _config     = config;
    }

    public bool ValidateTheme()
    {
        var themePath = Path.Combine(_pluginPath, _config.ResourceFile);
        return File.Exists(themePath);
    }

    private static ThemeBase ParseThemeBase(string? variant) =>
        string.Equals(variant?.Trim(), "dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeBase.Dark
            : ThemeBase.Light;
}

/// <summary>
/// Mirrors the structure of <c>themes.json</c>.
/// </summary>
public class ThemeManifest
{
    public string PackageName { get; set; } = "";
    public string Author      { get; set; } = "";
    public string Description { get; set; } = "";
    public List<ThemeConfig> Themes { get; set; } = new();
}

/// <summary>
/// Mirrors a single theme entry inside <c>themes.json</c>.
/// </summary>
public class ThemeConfig
{
    public string  Id           { get; set; } = "";
    public string  DisplayName  { get; set; } = "";
    public string? Description  { get; set; }
    /// <summary>"Light" or "Dark" (case-insensitive). Defaults to "Light".</summary>
    public string  BaseVariant  { get; set; } = "Light";
    public string  ResourceFile { get; set; } = "";
}
