using KafkaLens.App.ViewModels;
using KafkaLens.Core.DataAccess;
using KafkaLens.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using Serilog;

namespace KafkaLens.App
{
    public partial class App : Application
    {
        public IServiceProvider Services { get; }
        public new static App Current => (App)Application.Current;

        public App()
        {
            Services = ConfigureServices();

            InitializeComponent();
        }

        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddDbContext<KafkaContext>(opt => opt.UseSqlite("Data Source=KafkaDB.db;"));
            services.AddSingleton<IClusterService, LocalClusterService>();
            services.AddSingleton<ConsumerFactory>();
            services.AddSingleton<MainViewModel>();
            services.AddLogging();

            ConfigureLogging();

            return services.BuildServiceProvider();
        }

        private static void ConfigureLogging()
        {
            using var log = new LoggerConfiguration()
                .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u4}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();
            Log.Logger = log;
            Log.Information("The global logger has been configured");
        }
    }
}
