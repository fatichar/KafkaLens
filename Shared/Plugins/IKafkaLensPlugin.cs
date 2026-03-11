using System;

namespace KafkaLens.Shared.Plugins;

/// <summary>
/// Optional interface for plugin assemblies that need lifecycle hooks.
/// Implement on any public class in the plugin assembly; the loader will
/// instantiate it and call <see cref="Initialize"/> once the application
/// DI container is fully built, and <see cref="Shutdown"/> when the
/// application is closing.
/// </summary>
public interface IKafkaLensPlugin
{
    /// <summary>
    /// Called once after the application DI container has been built.
    /// Use <paramref name="services"/> to resolve application services.
    /// </summary>
    void Initialize(IServiceProvider services);

    /// <summary>
    /// Called once when the application is shutting down.
    /// Release any resources (threads, connections, file handles) acquired in
    /// <see cref="Initialize"/>.
    /// </summary>
    void Shutdown();
}
