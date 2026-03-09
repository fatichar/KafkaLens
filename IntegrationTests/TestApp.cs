using System;
using Avalonia;
using AvaloniaApp;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels;
using KafkaLens.ViewModels.Config;
using KafkaLens.ViewModels.Services;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Serilog;
using Avalonia.Markup.Xaml;
using KafkaLens.ViewModels.Messages;
using KafkaLens.Formatting;
using System.IO;

namespace IntegrationTests;

public class TestApp : App
{
    protected override IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        var config = new AppConfig();
        services.AddSingleton(config);

        var tempClusterInfoFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var clusterRepo = new ClusterInfoRepository(tempClusterInfoFile);
        services.AddSingleton<IClusterInfoRepository>(clusterRepo);

        var tempClientInfoFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        var clientRepo = new ClientInfoRepository(tempClientInfoFile);
        services.AddSingleton<IClientInfoRepository>(clientRepo);

        var settingsService = Substitute.For<ISettingsService>();
        services.AddSingleton(settingsService);

        var kafkaConfig = new KafkaConfig();
        settingsService.GetKafkaConfig().Returns(kafkaConfig);
        services.AddSingleton(kafkaConfig);

        var browserConfig = new BrowserConfig();
        settingsService.GetBrowserConfig().Returns(browserConfig);
        services.AddSingleton(browserConfig);

        var topicSettingsService = Substitute.For<ITopicSettingsService>();
        topicSettingsService.GetSettings(Arg.Any<string>(), Arg.Any<string>())
            .Returns(new KafkaLens.ViewModels.TopicSettings());
        services.AddSingleton(topicSettingsService);

        var localClient = new Fakes.FakeKafkaClient(clusterRepo);
        services.AddSingleton<IKafkaLensClient>(localClient);

        var savedMessagesClient = Substitute.For<ISavedMessagesClient>();
        services.AddSingleton(savedMessagesClient);

        FormatterFactory.AddFromPath(""); // Ensure formatter defaults are created
        services.AddSingleton(new MessageViewOptions
            {
                FormatterName = FormatterFactory.Instance.DefaultFormatter.Name
            }
        );

        services.AddSingleton<IClusterFactory, ClusterFactory>();
        services.AddSingleton<IClientFactory, ClientFactory>();
        var messageSaver = Substitute.For<IMessageSaver>();
        services.AddSingleton(messageSaver);

        services.AddSingleton<IFormatterService, FormatterService>();

        var updateService = Substitute.For<IUpdateService>();
        services.AddSingleton(updateService);
        services.AddSingleton<MainViewModel>();

        services.AddLogging();

        var logPath = "logs/testlog.txt";
        var logDirectory = Path.GetDirectoryName(logPath);
        if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
        {
            Directory.CreateDirectory(logDirectory);
        }

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .CreateLogger();

        return services.BuildServiceProvider();
    }
}
