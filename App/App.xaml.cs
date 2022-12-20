using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using KafkaLens.Core.Services;
using KafkaLens.Shared;
using KafkaLens.ViewModels;
using KafkaLens.ViewModels.DataAccess;
using Microsoft.Extensions.Configuration;
using Serilog;
using AppConfig = KafkaLens.App.Config.AppConfig;

namespace KafkaLens.App;

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