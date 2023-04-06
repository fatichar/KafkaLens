using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AvaloniaApp.Views;
using KafkaLens;
using KafkaLens.Core.Services;
using KafkaLens.Formatting;
using KafkaLens.Shared;
using KafkaLens.ViewModels;
using KafkaLens.ViewModels.DataAccess;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System;
using Avalonia.Logging;
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
                .AddJsonFile("appsettings.json")
                .Build();

            var config = new AppConfig();
            configuration.Bind(config);

            var services = new ServiceCollection();
            services.AddSingleton<ISettingsService, SettingsService>();

            services.AddDbContext<KafkaClientContext>(opt =>
                opt.UseSqlite($"Data Source={config.DatabasePath};",
                    b => b.MigrationsAssembly("ViewModels")));
            services.AddSingleton<IKafkaLensClient, LocalClient>();
            services.AddSingleton<ConsumerFactory>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<FormatterFactory>();
            services.AddLogging();

            ConfigureLogging();
            

            return services.BuildServiceProvider();
        }

        private static void ConfigureLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u4}] {Message:lj}{NewLine}{Exception}")
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
                desktop.MainWindow = new MainWindow {
                    DataContext = viewModel
                };
            }
            else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
            {
                singleViewPlatform.MainView = new MainView {
                    DataContext = viewModel
                };
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}