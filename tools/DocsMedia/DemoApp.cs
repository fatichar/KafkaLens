using KafkaLens.Formatting;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Services;
using KafkaLens.UI;
using KafkaLens.UI.Services;
using KafkaLens.ViewModels;
using KafkaLens.ViewModels.Config;
using KafkaLens.ViewModels.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Serilog;

namespace DocsMedia;

/// <summary>
/// The real KafkaLens <see cref="App"/>, wired with an in-memory demo Kafka client and
/// throwaway settings in a temp directory. No telemetry, no update checks, no network.
/// </summary>
public class DemoApp : App
{
    public static string Theme { get; set; } = "Light";
    public static string DataDir { get; } = Path.Combine(Path.GetTempPath(), "kafkalens-docsmedia");

    protected override IServiceProvider ConfigureServices()
    {
        if (Directory.Exists(DataDir)) Directory.Delete(DataDir, true);
        Directory.CreateDirectory(DataDir);

        Log.Logger = new LoggerConfiguration().MinimumLevel.Warning().WriteTo.Console().CreateLogger();

        var services = new ServiceCollection();
        services.AddSingleton(new AppConfig());

        var clusterRepo = new ClusterInfoRepository(Path.Combine(DataDir, "cluster_info.json"));
        clusterRepo.Add(new ClusterInfo("prod-eu", "Production (eu-west-1)", "b-1.prod-msk.eu-west-1.amazonaws.com:9098,b-2.prod-msk.eu-west-1.amazonaws.com:9098"));
        clusterRepo.Add(new ClusterInfo("staging", "Staging", "kafka.staging.internal:9092"));
        clusterRepo.Add(new ClusterInfo("local", "Local Docker", "localhost:9092"));
        services.AddSingleton<IClusterInfoRepository>(clusterRepo);

        var clientRepo = new ClientInfoRepository(Path.Combine(DataDir, "client_info.json"));
        services.AddSingleton<IClientInfoRepository>(clientRepo);

        var settings = new SettingsService(Path.Combine(DataDir, "settings.json"));
        settings.SetValue("Theme", Theme);
        settings.SetValue("AutoCheckForUpdates", "false");
        settings.SetValue(PreferencesViewModel.CONNECTION_CHECK_INTERVAL_SECONDS_KEY, "0");
        services.AddSingleton<ISettingsService>(settings);

        var kafkaConfig = settings.GetKafkaConfig();
        services.AddSingleton(kafkaConfig);
        var browserConfig = settings.GetBrowserConfig();
        browserConfig.DefaultFetchCount = 100;
        browserConfig.SavedMessagesDirectory = Path.Combine(DataDir, "saved");
        settings.SaveBrowserConfig(browserConfig);
        services.AddSingleton(browserConfig);

        services.AddSingleton<ITopicSettingsService>(new TopicSettingsService(Path.Combine(DataDir, "topic_settings.json")));
        services.AddSingleton<IKafkaLensClient>(new DemoKafkaClient(clusterRepo));
        services.AddSingleton(Substitute.For<ISavedMessagesClient>());

        FormatterFactory.AddFromPath("");
        services.AddSingleton(new MessageViewOptions { FormatterName = FormatterFactory.Instance.DefaultFormatter.Name });

        services.AddSingleton<IClusterFactory, ClusterFactory>();
        services.AddSingleton<IClientFactory, ClientFactory>();
        services.AddSingleton(Substitute.For<IMessageSaver>());
        services.AddSingleton<IFormatterService, FormatterService>();
        services.AddSingleton(Substitute.For<IUpdateService>());

        var pluginsDir = Path.Combine(DataDir, "plugins");
        Directory.CreateDirectory(pluginsDir);
        var extensionRegistry = new ExtensionRegistry();
        services.AddSingleton(extensionRegistry);
        services.AddSingleton(new PluginRegistry(pluginsDir, settings, extensionRegistry));
        services.AddSingleton(new PluginRepositoryClient());
        services.AddSingleton(new PluginInstaller(pluginsDir));
        services.AddSingleton(new RepositoryManager(settings));
        var themeService = new ThemeService(extensionRegistry, pluginsDir);
        services.AddSingleton<IThemeService>(themeService);
        // App keeps its theme service in a private field that only the production ConfigureServices sets.
        typeof(App).GetField("_themeService", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(this, themeService);
        services.AddSingleton<IAppLogService, AppLogService>();
        services.AddSingleton<MainViewModel>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }
}
