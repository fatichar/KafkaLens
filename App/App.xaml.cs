using KafkaLens.App.ViewModels;
using KafkaLens.Core.DataAccess;
using KafkaLens.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace KafkaLens.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public IServiceProvider Services { get; }
        
        public App()
        {
            Services = ConfigureServices();
        }

        /// <summary>
        /// Configures the services for the application.
        /// </summary>
        private static IServiceProvider ConfigureServices()
        {
            var services = new ServiceCollection();

            services.AddSingleton<ISettingsService, SettingsService>();
            services.AddDbContext<KafkaContext>(opt => opt.UseSqlite("Data Source=KafkaDB.db;"));
            services.AddSingleton<IClusterService, ClusterService>();
            services.AddSingleton<ConsumerFactory>();
            services.AddTransient<ClustersViewModel>();

            return services.BuildServiceProvider();
        }
    }
}
