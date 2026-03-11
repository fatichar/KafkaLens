using System.Collections.Generic;

namespace KafkaLens.Shared.Plugins;

/// <summary>
/// Platform-independent base-variant for a theme.
/// The UI layer maps this to the platform's native variant type
/// (e.g. <c>Avalonia.Styling.ThemeVariant</c>).
/// </summary>
public enum ThemeBase
{
    /// <summary>Light base variant.</summary>
    Light,

    /// <summary>Dark base variant.</summary>
    Dark
}

/// <summary>
/// Extension point: theme package provider that can host multiple themes.
/// More efficient than one class per theme - one plugin can serve many themes.
/// Implement and annotate with <c>[KafkaLensExtension(typeof(IThemePackage))]</c>.
/// </summary>
public interface IThemePackage
{
    /// <summary>Package name (shown in plugin manager).</summary>
    string PackageName { get; }

    /// <summary>Package author.</summary>
    string Author { get; }

    /// <summary>Package description.</summary>
    string Description { get; }

    /// <summary>
    /// Gets all themes provided by this package.
    /// </summary>
    IReadOnlyList<IThemeInfo> GetThemes();
}

/// <summary>
/// Theme information provided by a theme package.
/// </summary>
public interface IThemeInfo
{
    /// <summary>Unique identifier for the theme (used in settings).</summary>
    string Id { get; }

    /// <summary>Display name shown in the UI theme picker.</summary>
    string DisplayName { get; }

    /// <summary>Optional theme description.</summary>
    string Description { get; }

    /// <summary>
    /// Base variant used to select the underlying light or dark palette.
    /// The UI layer maps this to <c>ThemeVariant.Light</c> or <c>ThemeVariant.Dark</c>.
    /// </summary>
    ThemeBase BaseVariant { get; }

    /// <summary>
    /// Validates that theme resources are present and loadable.
    /// </summary>
    bool ValidateTheme();
}

/// <summary>
/// Legacy interface for single-theme plugins (kept for backward compatibility).
/// New plugins should prefer <see cref="IThemePackage"/> which supports multiple themes
/// per plugin.
/// </summary>
public interface ITheme : IThemeInfo
{
    /// <summary>Theme author (for single-theme plugins).</summary>
    string Author { get; }
}
