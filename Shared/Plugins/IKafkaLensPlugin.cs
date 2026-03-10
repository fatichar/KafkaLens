using System;

namespace KafkaLens.Shared.Plugins;

/// <summary>
/// Optional interface for plugin assemblies that need initialization logic.
/// Implement on any public class in the plugin assembly; the loader will
/// instantiate it and call <see cref="Initialize"/> after the assembly is loaded.
/// </summary>
public interface IKafkaLensPlugin
{
    /// <summary>
    /// Called once after the plugin assembly has been loaded.
    /// </summary>
    /// <param name="services">
    /// The application service provider, or <c>null</c> if not yet available.
    /// </param>
    void Initialize(IServiceProvider? services);
}
