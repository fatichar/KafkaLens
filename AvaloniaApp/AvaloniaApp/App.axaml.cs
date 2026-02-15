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
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.DataAccess;
using KafkaLens.ViewModels.Config;
using KafkaLens.ViewModels.Messages;
using KafkaLens.ViewModels.Services;
using Serilog;

namespace AvaloniaApp;

public partial class App : Application
{
    public IServiceProvider Services { get; }
    public new static App Current => (App)Application.Current!;

    public App()
    {
        Services = ConfigureServices();
    }

    private static IServiceProvider ConfigureServices()
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
        services.AddSingleton<ISettingsService>(new SettingsService(settingsFilePath));

        var topicSettingsFilePath = Path.Combine(kafkaLensDataPath, "topic_settings.json");
        services.AddSingleton<ITopicSettingsService>(new TopicSettingsService(topicSettingsFilePath));

        var pluginsPath = Path.Combine(kafkaLensDataPath, "Plugins");
        var pluginsDir = Directory.CreateDirectory(pluginsPath);
        AddLocalDependencies(services, clusterRepo, pluginsDir);

        FormatterFactory.AddFromPath(pluginsPath);
        services.AddSingleton(FormatterFactory.Instance);

        services.AddSingleton(new MessageViewOptions
            {
                FormatterName = FormatterFactory.Instance.DefaultFormatter.Name
            }
        );

        // services.AddSingleton<ConsumerFactory>();
        services.AddSingleton<IClusterFactory, ClusterFactory>();
        services.AddSingleton<IClientFactory, ClientFactory>();
        services.AddSingleton<IUpdateService, UpdateService>();
        services.AddSingleton<MainViewModel>();

        services.AddLogging();
        ConfigureLogging();

        return services.BuildServiceProvider();
    }

    private static void AddLocalDependencies(
        IServiceCollection services,
        IClusterInfoRepository clusterRepo,
        DirectoryInfo pluginsDir)
    {
        AddPlugins(services, pluginsDir);
        var localClientsAssembly = LoadLocalClientsAssembly();
        if (localClientsAssembly == null)
        {
            Log.Error("Could not load KafkaLens.LocalClient.dll");
            return;
        }

        IKafkaLensClient? localClient = CreateLocalClient(localClientsAssembly, clusterRepo);
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

    private static void AddPlugins(IServiceCollection services, DirectoryInfo pluginsDir)
    {
        // var pluginLoader = new PluginLoader(pluginsDir);
        // var plugins = pluginLoader.LoadPlugins();
        // foreach (var plugin in plugins)
        // {
        //     services.AddSingleton(plugin);
        // }

        var formattersPath = Path.Combine(pluginsDir.FullName, "Formatters");
        var formattersDir = Directory.CreateDirectory(formattersPath);
    }


    private static IKafkaLensClient? CreateLocalClient(Assembly assembly, IClusterInfoRepository clusterRepo)
    {
        var type = assembly.GetType("KafkaLens.Clients.LocalClient");
        if (type == null)
        {
            Log.Error("Could not find KafkaLens.Clients.LocalClient");
            return null;
        }

        var localClient = Activator.CreateInstance(type, clusterRepo);
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
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console(
                outputTemplate:
                "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u4}] {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        Log.Information("The global logger has been configured");
        System.Diagnostics.Debug.WriteLine("Log to console");
    }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private ResourceDictionary? _currentThemeResources;
    private string _currentThemeName = "";

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
        _currentThemeName = themeName;

        // Remove previously applied theme resources
        if (_currentThemeResources != null)
        {
            Resources.MergedDictionaries.Remove(_currentThemeResources);
            _currentThemeResources = null;
        }

        // Determine which theme file to load and which base variant to use
        string themeFileToLoad;
        if (themeName == "System")
        {
            RequestedThemeVariant = ThemeVariant.Default;
            var isDark = ActualThemeVariant == ThemeVariant.Dark;
            themeFileToLoad = isDark ? "Dark" : "Light";
            Log.Information("System theme detected as: {Variant}", themeFileToLoad);
        }
        else
        {
            RequestedThemeVariant = themeName == "Dark" ? ThemeVariant.Dark : ThemeVariant.Light;
            themeFileToLoad = themeName;
        }

        // Load and apply theme resource dictionary
        var themeDict = LoadThemeFromFile(themeFileToLoad);
        if (themeDict != null)
        {
            Resources.MergedDictionaries.Add(themeDict);
            _currentThemeResources = themeDict;
            Log.Information("Theme {ThemeName} applied. MergedDictionaries count: {Count}", themeName, Resources.MergedDictionaries.Count);
        }
        else
        {
            Log.Error("Theme {ThemeName} could not be applied - dictionary was null", themeName);
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

        var viewModel = Services.GetRequiredService<MainViewModel>();
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