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

            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            appDataPath = Path.Combine(appDataPath, "KafkaLens");

            var clusterRepo = new ClusterInfoRepository(Path.Combine(appDataPath, config.ClusterInfoFilePath));
            services.AddSingleton<IClusterInfoRepository>(clusterRepo);

            var clientRepo = new ClientInfoRepository(Path.Combine(appDataPath, config.ClientInfoFilePath));
            services.AddSingleton<IClientInfoRepository>(clientRepo);

            services.AddSingleton<ISettingsService, SettingsService>();

            var pluginsPath = Path.Combine(appDataPath, "Plugins");
            var pluginsDir = Directory.CreateDirectory(pluginsPath);
            AddLocalDependencies(services, clusterRepo, pluginsDir);

            var formatterFactory = new FormatterFactory(pluginsPath);
            services.AddSingleton(formatterFactory);

            // services.AddSingleton<ConsumerFactory>();
            services.AddSingleton<IClusterFactory, ClusterFactory>();
            services.AddSingleton<IClientFactory, ClientFactory>();
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
                services.AddSingleton(savedMessagesClient);
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