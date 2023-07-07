using System.Collections.Generic;
using Avalonia;
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
using Avalonia.Logging;
using KafkaLens.Clients;
using KafkaLens.Core.DataAccess;
using KafkaLens.Shared.DataAccess;
using KafkaLens.ViewModels.Config;
using Serilog;

namespace AvaloniaApp
{
    public partial class App : Application
    {
        public IServiceProvider Services { get; }
        public new static App Current => (App)Application.Current;

        public App()
        {
            Services = ConfigureServices();
        }

        private static IServiceProvider ConfigureServices()
        {
            var configuration = new ConfigurationBuilder()
                .AddEnvironmentVariables()
                .Build();

            var config = new AppConfig();
            configuration.Bind(config);

            var services = new ServiceCollection();
            services.AddSingleton(config);

            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            appDataDir = Path.Combine(appDataDir, "KafkaLens");
            Directory.CreateDirectory(appDataDir);

            var clusterRepo = new ClusterInfoRepository(Path.Combine(appDataDir, config.ClusterInfoFilePath));
            services.AddSingleton<IClusterInfoRepository>(clusterRepo);

            var clientRepo = new ClientInfoRepository(Path.Combine(appDataDir, config.ClientInfoFilePath));
            services.AddSingleton<IClientInfoRepository>(clientRepo);

            services.AddSingleton<ISettingsService, SettingsService>();

            AddConditionalDependencies(services, clusterRepo);

            // services.AddSingleton<ConsumerFactory>();
            services.AddSingleton<IClusterFactory, ClusterFactory>();
            services.AddSingleton<IClientFactory, ClientFactory>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<FormatterFactory>();
            services.AddLogging();
            ConfigureLogging();

            return services.BuildServiceProvider();
        }

        private static void AddConditionalDependencies(IServiceCollection services, IClusterInfoRepository clusterRepo)
        {
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
                services.AddSingleton(savedMessagesClient);
            }
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
                assembly = Assembly.LoadFrom("./KafkaLens.LocalClient.dll");
            }
            catch (Exception e)
            {
                Log.Error("Could not load KafkaLens.LocalClient.dll");
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

        public override void OnFrameworkInitializationCompleted()
        {
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
}