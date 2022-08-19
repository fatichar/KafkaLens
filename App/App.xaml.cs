using KafkaLens.App.ViewModels;
using KafkaLens.Core.DataAccess;
using KafkaLens.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;

namespace KafkaLens.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public IServiceProvider Services { get; }
        public new static App Current => (App)Application.Current;

        public App()
        {
            Services = ConfigureServices();

            InitializeComponent();
        }

        /// <summary>
        /// Configures the services for the application.
        /// </summary>
        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddDbContext<KafkaContext>(opt => opt.UseSqlite("Data Source=KafkaDB.db;"));
            services.AddSingleton<IClusterService, LocalClusterService>();
            services.AddSingleton<ConsumerFactory>();
            services.AddSingleton<MainViewModel>();
            services.AddLogging();

            return services.BuildServiceProvider();
        }
    }
}
