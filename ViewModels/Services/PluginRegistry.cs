using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using KafkaLens.Formatting;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Plugins;
using KafkaLens.Shared.Services;
using Serilog;

namespace KafkaLens.ViewModels.Services;

public class PluginRegistry : IDisposable
{
    private readonly string _pluginsDir;
    private readonly ISettingsService _settings;
    private readonly ExtensionRegistry _extensionRegistry;
    private readonly HashSet<string> _registeredIds = [];

    /// <summary>
    /// Holds all instantiated <see cref="IKafkaLensPlugin"/> implementations so that
    /// <see cref="InitializeAll"/> and <see cref="Dispose"/> can invoke them in order.
    /// </summary>
    private readonly List<IKafkaLensPlugin> _pluginInstances = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public PluginRegistry(string pluginsDir, ISettingsService settings,
        ExtensionRegistry? extensionRegistry = null)
    {
        _pluginsDir        = pluginsDir;
        _settings          = settings;
        _extensionRegistry = extensionRegistry ?? new ExtensionRegistry();
    }

    /// <summary>
    /// Discovers all installed plugins by scanning:
    /// 1. Subdirectories: <c>plugins/{id}/</c> — looks for any <c>*.dll</c> inside
    /// 2. Flat DLLs (backward compat): <c>plugins/{id}.dll</c>
    /// For each DLL: reads <see cref="KafkaLensPluginAttribute"/>, loads optional
    /// <c>plugin.json</c>, registers <see cref="KafkaLensExtensionAttribute"/> extensions,
    /// and records any <see cref="IKafkaLensPlugin"/> implementations for later
    /// initialisation via <see cref="InitializeAll"/>.
    /// </summary>
    public IReadOnlyList<PluginInfo> LoadPlugins()
    {
        if (!Directory.Exists(_pluginsDir))
            return [];

        var pluginStates = _settings.GetPluginSettings().PluginStates;
        var result = new List<PluginInfo>();

        // 1. Folder-based plugins: plugins/{id}/ subdirectories
        foreach (var subDir in Directory.EnumerateDirectories(_pluginsDir))
        {
            var folderId = Path.GetFileName(subDir);

            // Check for declarative theme package first (themes.json)
            var themesJsonPath = Path.Combine(subDir, "themes.json");
            if (File.Exists(themesJsonPath))
            {
                try
                {
                    var info = LoadDeclarativeThemePackage(subDir, folderId, pluginStates);
                    if (info != null)
                        result.Add(info);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to load declarative theme package from folder {Folder}", subDir);
                }
                continue; // Skip DLL loading for declarative packages
            }

            // Look for plugin.dll first, then any .dll in the folder
            var dll = Path.Combine(subDir, "plugin.dll");
            if (!File.Exists(dll))
                dll = Directory.EnumerateFiles(subDir, "*.dll").FirstOrDefault() ?? "";

            if (string.IsNullOrEmpty(dll)) continue;

            try
            {
                var info = LoadPluginFromDll(dll, subDir, pluginStates);
                if (info != null)
                    result.Add(info);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load plugin from folder {Folder}", subDir);
            }
        }

        // 2. Legacy flat DLLs: plugins/{id}.dll
        foreach (var dll in Directory.EnumerateFiles(_pluginsDir, "*.dll"))
        {
            try
            {
                var info = LoadPluginFromDll(dll, folderPath: "", pluginStates);
                if (info != null)
                    result.Add(info);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to load plugin metadata from {Dll}", dll);
            }
        }

        return result;
    }

    /// <summary>
    /// Calls <see cref="IKafkaLensPlugin.Initialize"/> on every plugin instance collected
    /// during <see cref="LoadPlugins"/>.  Call this once after the application DI
    /// container has been built so that plugins receive a fully-populated
    /// <see cref="IServiceProvider"/>.
    /// </summary>
    public void InitializeAll(IServiceProvider services)
    {
        foreach (var plugin in _pluginInstances)
        {
            try
            {
                plugin.Initialize(services);
                Log.Information("Initialized Plugin: {Type}", plugin.GetType().FullName);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to initialize IKafkaLensPlugin {Type}", plugin.GetType().FullName);
            }
        }
    }

    /// <summary>
    /// Calls <see cref="IKafkaLensPlugin.Shutdown"/> on every loaded plugin instance.
    /// Automatically invoked when the registry is disposed.
    /// </summary>
    public void Dispose()
    {
        foreach (var plugin in _pluginInstances)
        {
            try
            {
                plugin.Shutdown();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error during shutdown of IKafkaLensPlugin {Type}", plugin.GetType().FullName);
            }
        }
        _pluginInstances.Clear();
    }

    private PluginInfo? LoadPluginFromDll(string dll, string folderPath,
        Dictionary<string, bool> pluginStates)
    {
        var assembly = Assembly.LoadFrom(dll);
        var attr     = assembly.GetCustomAttribute<KafkaLensPluginAttribute>();

        // Try to read plugin.json manifest (only for folder-based plugins)
        PluginManifest? manifest = null;
        if (!string.IsNullOrEmpty(folderPath))
        {
            var manifestPath = Path.Combine(folderPath, "plugin.json");
            if (File.Exists(manifestPath))
            {
                try
                {
                    var json = File.ReadAllText(manifestPath);
                    manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to parse plugin.json in {Folder}", folderPath);
                }
            }
        }

        // Resolve metadata: manifest wins over attribute, attribute wins over filename
        var id       = NonEmpty(manifest?.Id,          attr?.Id)          ?? Path.GetFileNameWithoutExtension(dll);
        var name     = NonEmpty(manifest?.Name,        attr?.Name)        ?? id;
        var version  = NonEmpty(manifest?.Version,     attr?.Version)     ?? "";
        var author   = NonEmpty(manifest?.Author,      attr?.Author)      ?? "";
        var desc     = NonEmpty(manifest?.Description, attr?.Description) ?? "";
        var homepage = NonEmpty(manifest?.Homepage,    null);
        var category = NonEmpty(manifest?.Category,    null)              ?? "";

        // Check for icon
        var iconPath = "";
        if (!string.IsNullOrEmpty(folderPath))
        {
            var candidate = Path.Combine(folderPath, "icon.png");
            if (File.Exists(candidate)) iconPath = candidate;
        }

        var isEnabled = pluginStates.TryGetValue(id, out var enabled) ? enabled : true;

        if (isEnabled && _registeredIds.Add(id))
        {
            RegisterExtensions(assembly);
            CollectPluginInstances(assembly);
        }

        return new PluginInfo
        {
            Id          = id,
            Name        = name,
            Version     = version,
            Author      = author,
            Description = desc,
            Category    = category,
            FilePath    = dll,
            FolderPath  = folderPath,
            IconPath    = iconPath,
            Homepage    = homepage,
            IsEnabled   = isEnabled
        };
    }

    private void RegisterExtensions(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            if (type.IsAbstract || type.IsInterface) continue;

            // Explicit registration via [KafkaLensExtension] attribute.
            var extensionAttrs = type.GetCustomAttributes<KafkaLensExtensionAttribute>();
            foreach (var extAttr in extensionAttrs)
            {
                TryRegister(type, extAttr.ExtensionType);
            }

            // Implicit fallback for IMessageFormatter so that formatter plugins written
            // before [KafkaLensExtension] was introduced continue to work without changes.
            if (typeof(IMessageFormatter).IsAssignableFrom(type)
                && !type.GetCustomAttributes<KafkaLensExtensionAttribute>()
                        .Any(a => a.ExtensionType == typeof(IMessageFormatter)))
            {
                TryRegister(type, typeof(IMessageFormatter));
            }
        }
    }

    private void TryRegister(Type type, Type extensionType)
    {
        try
        {
            var instance = Activator.CreateInstance(type);
            if (instance == null) return;

            var registerMethod = typeof(ExtensionRegistry)
                .GetMethod(nameof(ExtensionRegistry.Register))!
                .MakeGenericMethod(extensionType);

            registerMethod.Invoke(_extensionRegistry, [instance]);
            Log.Information("Registered extension {Type} for {Interface}",
                type.FullName, extensionType.Name);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to register extension {Type}", type.FullName);
        }
    }

    /// <summary>
    /// Instantiates every <see cref="IKafkaLensPlugin"/> implementation in the assembly
    /// and stores the instances for later calls to <see cref="InitializeAll"/> /
    /// <see cref="Dispose"/>.  Does NOT call <see cref="IKafkaLensPlugin.Initialize"/>
    /// here because the DI container may not yet be available.
    /// </summary>
    private void CollectPluginInstances(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            if (!typeof(IKafkaLensPlugin).IsAssignableFrom(type) || type.IsAbstract) continue;
            try
            {
                if (Activator.CreateInstance(type) is IKafkaLensPlugin instance)
                {
                    _pluginInstances.Add(instance);
                    Log.Debug("Collected IKafkaLensPlugin instance: {Type}", type.FullName);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to instantiate IKafkaLensPlugin {Type}", type.FullName);
            }
        }
    }

    private PluginInfo? LoadDeclarativeThemePackage(string subDir, string folderId,
        Dictionary<string, bool> pluginStates)
    {
        try
        {
            var manifestPath = Path.Combine(subDir, "plugin.json");
            PluginManifest? manifest = null;
            if (File.Exists(manifestPath))
            {
                var json = File.ReadAllText(manifestPath);
                manifest = JsonSerializer.Deserialize<PluginManifest>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }

            var package = new DeclarativeThemePackageWrapper(subDir);

            var id       = NonEmpty(manifest?.Id,          folderId)            ?? folderId;
            var name     = NonEmpty(manifest?.Name,        package.PackageName) ?? id;
            var version  = NonEmpty(manifest?.Version,     "")                  ?? "";
            var author   = NonEmpty(manifest?.Author,      package.Author)      ?? "";
            var desc     = NonEmpty(manifest?.Description, package.Description) ?? "";
            var homepage = NonEmpty(manifest?.Homepage,    null);
            var category = NonEmpty(manifest?.Category,    null)                ?? "";

            var iconPath = "";
            var iconFile = Path.Combine(subDir, "icon.png");
            if (File.Exists(iconFile)) iconPath = iconFile;

            var isEnabled = pluginStates.TryGetValue(id, out var enabled) ? enabled : true;

            if (isEnabled && _registeredIds.Add(id))
            {
                _extensionRegistry.Register<IThemePackage>(package);
                Log.Information("Registered declarative theme package: {Id} - {Name}", id, name);
            }

            return new PluginInfo
            {
                Id          = id,
                Name        = name,
                Version     = version,
                Author      = author,
                Description = desc,
                Category    = category,
                FilePath    = "",
                FolderPath  = subDir,
                IconPath    = iconPath,
                Homepage    = homepage,
                IsEnabled   = isEnabled
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to load declarative theme package from {Folder}", subDir);
            return null;
        }
    }

    public bool IsEnabled(string id)
    {
        var states = _settings.GetPluginSettings().PluginStates;
        return states.TryGetValue(id, out var enabled) ? enabled : true;
    }

    public void SetEnabled(string id, bool enabled)
    {
        var ps = _settings.GetPluginSettings();
        ps.PluginStates[id] = enabled;
        _settings.SavePluginSettings(ps);
    }

    private static string? NonEmpty(string? first, string? second)
    {
        if (!string.IsNullOrWhiteSpace(first)) return first;
        if (!string.IsNullOrWhiteSpace(second)) return second;
        return null;
    }
}