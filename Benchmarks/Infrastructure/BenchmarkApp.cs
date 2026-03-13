using System;
using System.IO;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using AvaloniaApp;
using KafkaLens.Formatting;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Services;
using KafkaLens.ViewModels;
using KafkaLens.ViewModels.Config;
using KafkaLens.ViewModels.Messages;
using KafkaLens.ViewModels.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Serilog;

namespace Benchmarks.Infrastructure;

/// <summary>
/// Avalonia application used by ViewModel-level benchmarks.  Mirrors the setup in
/// IntegrationTests/TestApp.cs but uses a dispatcher-aware <see cref="BenchmarkKafkaClient"/>
/// so that <see cref="MessageStream"/> filling is properly deferred to the UI thread.
/// </summary>
public sealed class BenchmarkApp : App
{
    /// <summary>
    /// Set before starting the application; invoked once
    /// <see cref="OnFrameworkInitializationCompleted"/> has run.
    /// </summary>
    internal static Action? OnInitialized;

    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        OnInitialized?.Invoke();
    }

    protected override IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        var config = new AppConfig();
        services.AddSingleton(config);

        var tempClusterFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "-bench-clusters.json");
        var clusterRepo = new ClusterInfoRepository(tempClusterFile);
        services.AddSingleton<IClusterInfoRepository>(clusterRepo);

        var tempClientFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + "-bench-clients.json");
        var clientRepo = new ClientInfoRepository(tempClientFile);
        services.AddSingleton<IClientInfoRepository>(clientRepo);

        var settingsService = Substitute.For<ISettingsService>();
        var kafkaConfig = new KafkaConfig();
        var browserConfig = new BrowserConfig();
        settingsService.GetKafkaConfig().Returns(kafkaConfig);
        settingsService.GetBrowserConfig().Returns(browserConfig);
        settingsService.GetPluginSettings().Returns(new PluginSettings());
        services.AddSingleton(settingsService);
        services.AddSingleton(kafkaConfig);
        services.AddSingleton(browserConfig);

        var topicSettingsService = Substitute.For<ITopicSettingsService>();
        topicSettingsService.GetSettings(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new KafkaLens.ViewModels.TopicSettings());
        services.AddSingleton(topicSettingsService);

        var benchmarkClient = new BenchmarkKafkaClient(clusterRepo);
        services.AddSingleton<IKafkaLensClient>(benchmarkClient);

        var savedMessagesClient = Substitute.For<ISavedMessagesClient>();
        services.AddSingleton(savedMessagesClient);

        FormatterFactory.AddFromPath("");
        services.AddSingleton(new MessageViewOptions
        {
            FormatterName = FormatterFactory.Instance.DefaultFormatter.Name
        });

        services.AddSingleton<IClusterFactory, ClusterFactory>();
        services.AddSingleton<IClientFactory, ClientFactory>();
        services.AddSingleton(Substitute.For<IMessageSaver>());
        services.AddSingleton<IFormatterService, FormatterService>();
        services.AddSingleton(Substitute.For<IUpdateService>());

        var pluginsDir = Path.Combine(Path.GetTempPath(), "kafkalens-bench-plugins");
        Directory.CreateDirectory(pluginsDir);
        var extensionRegistry = new ExtensionRegistry();
        services.AddSingleton(extensionRegistry);
        services.AddSingleton(new PluginRegistry(pluginsDir, settingsService, extensionRegistry));
        services.AddSingleton(new PluginRepositoryClient());
        services.AddSingleton(new PluginInstaller(pluginsDir));
        services.AddSingleton(new RepositoryManager(settingsService));
        services.AddSingleton<IThemeService>(new AvaloniaApp.Services.ThemeService(extensionRegistry, pluginsDir));

        services.AddSingleton<MainViewModel>();
        services.AddLogging();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Warning()
            .WriteTo.Console()
            .CreateLogger();

        return services.BuildServiceProvider();
    }
}
