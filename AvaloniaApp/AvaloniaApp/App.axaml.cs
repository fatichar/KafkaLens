using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Views;
using KafkaLens.Formatting;
using KafkaLens.Shared;
using KafkaLens.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Reflection;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.DataAccess;
using KafkaLens.ViewModels.Config;
using KafkaLens.ViewModels.Messages;
using KafkaLens.ViewModels.Services;
using KafkaLens.Shared.Services;
using AvaloniaApp.Services;
using Serilog;
using KafkaLens.Shared.Models;

namespace AvaloniaApp;

public partial class App : Application
{
    public IServiceProvider Services { get; protected set; }
    public new static App Current => (App)Application.Current!;
    private IThemeService? _themeService;

    public App()
    {
        // Don't call ConfigureServices here to allow derived TestApp classes
        // to completely bypass logic before initialization if needed.
        // It's assigned later in actual startup if not already.
    }

    public override void Initialize()
    {
        if (Services == null)
            Services = ConfigureServices();

        AvaloniaXamlLoader.Load(this);
    }

    protected virtual IServiceProvider ConfigureServices()
    {
        var configuration = new ConfigurationBuilder()
            .AddEnvironmentVariables()
            .AddJsonFile("appsettings.json")
            .Build();

        var config = new AppConfig();
        configuration.Bind(config);

        var services = new ServiceCollection();
        services.AddSingleton(config);

        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var kafkaLensDataPath = Path.Combine(appDataPath, "KafkaLens");
        // Create the directory if it doesn't exist
        Directory.CreateDirectory(kafkaLensDataPath);

        var clusterRepo = new ClusterInfoRepository(Path.Combine(kafkaLensDataPath, config.ClusterInfoFilePath));
        services.AddSingleton<IClusterInfoRepository>(clusterRepo);

        var clientRepo = new ClientInfoRepository(Path.Combine(kafkaLensDataPath, config.ClientInfoFilePath));
        services.AddSingleton<IClientInfoRepository>(clientRepo);

        var settingsFilePath = Path.Combine(kafkaLensDataPath, "settings.json");
        var settingsService = new SettingsService(settingsFilePath);
        services.AddSingleton<ISettingsService>(settingsService);

        var kafkaConfig = settingsService.GetKafkaConfig();
        services.AddSingleton(kafkaConfig);

        var browserConfig = settingsService.GetBrowserConfig();
        services.AddSingleton(browserConfig);

        var topicSettingsFilePath = Path.Combine(kafkaLensDataPath, "topic_settings.json");
        services.AddSingleton<ITopicSettingsService>(new TopicSettingsService(topicSettingsFilePath));

        AddLocalDependencies(services, clusterRepo, kafkaConfig);

        var pluginManagerDir = Path.Combine(kafkaLensDataPath, "plugins");
        Directory.CreateDirectory(pluginManagerDir);
        var extensionRegistry = new ExtensionRegistry();
        var pluginRegistry    = new PluginRegistry(pluginManagerDir, settingsService, extensionRegistry);

        // Load enabled plugins so their extensions are registered
        _ = pluginRegistry.LoadPlugins();

        // Initialize ThemeService after plugins are loaded, passing the resolved plugins directory
        // so it can locate plugin XAML resources without hard-coding the path.
        _themeService = new ThemeService(extensionRegistry, pluginManagerDir);
        services.AddSingleton<IThemeService>(_themeService);

        services.AddSingleton(new MessageViewOptions
            {
                FormatterName = FormatterFactory.Instance.DefaultFormatter.Name
            }
        );
        var repoClient        = new PluginRepositoryClient();
        var pluginInstaller   = new PluginInstaller(pluginManagerDir, settingsService);
        var repoManager       = new RepositoryManager(settingsService);
        services.AddSingleton(extensionRegistry);
        services.AddSingleton(pluginRegistry);
        services.AddSingleton(repoClient);
        services.AddSingleton(pluginInstaller);
        services.AddSingleton(repoManager);

        services.AddSingleton<IClusterFactory, ClusterFactory>();
        services.AddSingleton<IClientFactory, ClientFactory>();
        services.AddSingleton<IMessageSaver, MessageSaver>();
        services.AddSingleton<IFormatterService, FormatterService>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<MainViewModel>();

        services.AddLogging();
        ConfigureLogging();

        var provider = services.BuildServiceProvider();

        // Now that the DI container is fully built, give plugins access to it.
        pluginRegistry.InitializeAll(provider);

        // Bridge plugin-provided formatters into FormatterFactory.
        // Must run after InitializeAll so plugins have had a chance to register
        // extensions inside their Initialize(IServiceProvider) method.
        foreach (var formatter in extensionRegistry.GetExtensions<IMessageFormatter>())
            FormatterFactory.Instance.AddFormatter(formatter);

        return provider;
    }

    private static void AddLocalDependencies(
        IServiceCollection services,
        IClusterInfoRepository clusterRepo,
        KafkaConfig kafkaConfig)
    {
        var localClientsAssembly = LoadLocalClientsAssembly();
        if (localClientsAssembly == null)
        {
            Log.Error("Could not load KafkaLens.LocalClient.dll");
            return;
        }

        IKafkaLensClient? localClient = CreateLocalClient(localClientsAssembly, clusterRepo, kafkaConfig);
        if (localClient != null)
        {
            services.AddSingleton(localClient);
        }
        ISavedMessagesClient? savedMessagesClient = CreateSavedMessagesClient(localClientsAssembly);
        if (savedMessagesClient != null)
        {
            services.AddSingleton<ISavedMessagesClient>(savedMessagesClient);
        }
    }

    private static IKafkaLensClient? CreateLocalClient(Assembly assembly, IClusterInfoRepository clusterRepo, KafkaConfig kafkaConfig)
    {
        var type = assembly.GetType("KafkaLens.Clients.LocalClient");
        if (type == null)
        {
            Log.Error("Could not find KafkaLens.Clients.LocalClient");
            return null;
        }

        var localClient = Activator.CreateInstance(type, clusterRepo, kafkaConfig);
        if (localClient == null)
        {
            Log.Error("Could not create instance of KafkaLens.Clients.LocalClient");
            return null;
        }

        return (IKafkaLensClient)localClient;
    }

    private static ISavedMessagesClient? CreateSavedMessagesClient(Assembly assembly)
    {
        var type = assembly.GetType("KafkaLens.Clients.SavedMessagesClient");
        if (type == null)
        {
            Log.Error("Could not find KafkaLens.Clients.SavedMessagesClient");
            return null;
        }

        var savedMessagesClient = Activator.CreateInstance(type);
        if (savedMessagesClient == null)
        {
            Log.Error("Could not create instance of KafkaLens.Clients.SavedMessagesClient");
            return null;
        }

        return savedMessagesClient as ISavedMessagesClient;
    }

    private static Assembly? LoadLocalClientsAssembly()
    {
        Assembly? assembly = null;
        try
        {
            var baseDir = AppContext.BaseDirectory;
            var dllPath = Path.Combine(baseDir, "KafkaLens.LocalClient.dll");
            assembly = Assembly.LoadFrom(dllPath);
        }
        catch (Exception e)
        {
            Log.Error(e, "Could not load KafkaLens.LocalClient.dll");
        }

        return assembly;
    }

    private static void ConfigureLogging()
    {
        var logPath = GetLogPath();
        var logDirectory = Path.GetDirectoryName(logPath);

        if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
            }
            catch
            {
                // Fallback to local logs if AppData creation fails
                logPath = "logs/log.txt";
            }
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u4}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("The global logger has been configured with log path: {LogPath}", logPath);
        System.Diagnostics.Debug.WriteLine("Log to console");
    }

    private static string GetLogPath()
    {
#if RELEASE
        // In Release, use AppData\Local\KafkaLens
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "KafkaLens", "logs", "log.txt");
#else
        // In Debug, use local logs directory
        return "logs/log.txt";
#endif
    }

    private ResourceDictionary? currentThemeResources;
    private string currentThemeName = "";

    private ResourceDictionary? LoadThemeFromFile(string themeName)
    {
        // Try embedded resource first (built-in themes)
        var uri = $"avares://AvaloniaApp/Themes/{themeName}.axaml";
        Log.Information("Attempting to load theme {ThemeName} from {Uri}", themeName, uri);
        try
        {
            var resourceDict = (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(uri));
            Log.Information("Successfully loaded built-in theme {ThemeName}, keys: {Count}", themeName, resourceDict.Count);
            return resourceDict;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load built-in theme {ThemeName} from {Uri}", themeName, uri);
        }

        // Try external file (user/store themes)
        var externalPath = Path.Combine(AppContext.BaseDirectory, "Themes", $"{themeName}.axaml");
        Log.Information("Trying external theme path: {Path}, exists: {Exists}", externalPath, File.Exists(externalPath));
        if (File.Exists(externalPath))
        {
            try
            {
                var xaml = File.ReadAllText(externalPath);
                var resourceDict = (ResourceDictionary)AvaloniaRuntimeXamlLoader.Load(xaml);
                Log.Information("Successfully loaded external theme {ThemeName}", themeName);
                return resourceDict;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not load external theme {ThemeName} from {Path}", themeName, externalPath);
            }
        }

        Log.Error("Failed to load theme {ThemeName} from any source", themeName);
        return null;
    }

    private void ApplyTheme(string themeName)
    {
        Log.Information("ApplyTheme called with: {ThemeName}", themeName);
        currentThemeName = themeName;

        // Remove previously applied theme resources
        if (currentThemeResources != null)
        {
            Resources.MergedDictionaries.Remove(currentThemeResources);
            currentThemeResources = null;
        }

        // Try to load theme using ThemeService (supports both built-in and plugin themes)
        var themeDict = _themeService?.LoadThemeResources(themeName);

        if (themeDict != null)
        {
            // Check if this theme dictionary is already added to prevent duplicates
            var resourceDict = themeDict as ResourceDictionary;
            if (resourceDict != null && !Resources.MergedDictionaries.Contains(resourceDict))
            {
                // Set base theme variant based on theme info
                var themeInfo = _themeService?.GetTheme(themeName);
                if (themeInfo != null)
                    RequestedThemeVariant = ThemeService.ThemeBaseToVariant(themeInfo.BaseVariant);

                Resources.MergedDictionaries.Add(resourceDict);
                currentThemeResources = resourceDict;
                Log.Information("Theme {ThemeName} applied successfully", themeName);
            }
            else
            {
                Log.Warning("Theme {ThemeName} dictionary already exists or is null", themeName);
            }
        }
        else if (_themeService?.GetTheme(themeName) != null)
        {
            // Theme is registered but has no resource dictionary (e.g. "System").
            // Tell Avalonia to follow the OS variant, then load Light/Dark resources to match.
            RequestedThemeVariant = ThemeVariant.Default;

            var isDark = PlatformSettings?.GetColorValues().ThemeVariant == Avalonia.Platform.PlatformThemeVariant.Dark;
            var variantName = isDark ? "Dark" : "Light";
            Log.Information("System theme: OS variant is {Variant}, loading {VariantName} resources", isDark ? "Dark" : "Light", variantName);

            var variantDict = _themeService!.LoadThemeResources(variantName) as ResourceDictionary;
            if (variantDict != null && !Resources.MergedDictionaries.Contains(variantDict))
            {
                Resources.MergedDictionaries.Add(variantDict);
                currentThemeResources = variantDict;
            }
            Log.Information("Theme {ThemeName} applied via OS-matched resources ({VariantName})", themeName, variantName);
        }
        else
        {
            // Fallback to built-in theme loading if ThemeService fails
            Log.Warning("ThemeService failed to load {ThemeName}, falling back to built-in loading", themeName);
            themeDict = LoadThemeFromFile(themeName);

            if (themeDict != null)
            {
                // Set base variant for built-in themes
                if (themeName == "System")
                {
                    RequestedThemeVariant = ThemeVariant.Default;
                }
                else
                {
                    RequestedThemeVariant = themeName is "Dark" or "Gray" ? ThemeVariant.Dark : ThemeVariant.Light;
                }

                Resources.MergedDictionaries.Add(themeDict as ResourceDictionary);
                currentThemeResources = themeDict as ResourceDictionary;
                Log.Information("Theme {ThemeName} applied via fallback built-in loading", themeName);
            }
            else
            {
                Log.Error("Theme {ThemeName} could not be applied from any source", themeName);

                // Ultimate fallback to Light theme
                if (themeName != "Light")
                {
                    Log.Information("Falling back to Light theme");
                    ApplyTheme("Light");
                }
            }
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var settingsService = Services.GetRequiredService<ISettingsService>();
        var theme = settingsService.GetValue("Theme") ?? "Light";
        ApplyTheme(theme);

        WeakReferenceMessenger.Default.Register<ThemeChangedMessage>(this, (r, m) =>
        {
            ApplyTheme(m.Value);
        });

        // When the OS light/dark preference changes, re-apply resources if "System" theme is active.
        if (PlatformSettings != null)
        {
            PlatformSettings.ColorValuesChanged += (_, _) =>
            {
                if (currentThemeName == "System")
                {
                    Log.Information("OS color scheme changed; re-applying System theme resources");
                    ApplyTheme("System");
                }
            };
        }

        var viewModel = Services.GetRequiredService<MainViewModel>();

        MainViewModel.ShowPreferencesDialog = (vm) =>
        {
            var window = new PreferencesWindow
            {
                DataContext = vm
            };
            vm.CloseAction = window.Close;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                window.ShowDialog(desktop.MainWindow);
            }
        };

        MainViewModel.ShowPluginManagerDialog = (vm) =>
        {
            Log.Information("ShowPluginManagerDialog called with PluginManagerViewModel");
            var window = new PluginManagerWindow { DataContext = vm };
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                window.ShowDialog(desktop.MainWindow);
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = viewModel
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}