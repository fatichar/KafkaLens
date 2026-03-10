using Avalonia.Styling;
using Avalonia.Controls;
using System.Collections.Generic;
using KafkaLens.Shared.Plugins;

namespace AvaloniaApp.Plugins;

/// <summary>
/// Extension point: theme package provider that can host multiple themes.
/// More efficient than one class per theme - one plugin can serve many themes.
/// Implement and annotate with <c>[KafkaLensExtension(typeof(IThemePackage))]</c>.
/// </summary>
public interface IThemePackage : KafkaLens.Shared.Plugins.IThemePackage
{
    // This interface extends the Shared version to add Avalonia-specific methods
}

/// <summary>
/// Theme information provided by a theme package.
/// </summary>
public interface IThemeInfo : KafkaLens.Shared.Plugins.IThemeInfo
{
    /// <summary>
    /// Loads the theme resources. Returns null if loading fails.
    /// </summary>
    ResourceDictionary? LoadThemeResources();
}

/// <summary>
/// Legacy interface for single-theme plugins (kept for backward compatibility).
/// New plugins should prefer IThemePackage for multiple themes.
/// </summary>
public interface ITheme : IThemeInfo, KafkaLens.Shared.Plugins.ITheme
{
    // Inherits from both Avalonia and Shared versions
}
