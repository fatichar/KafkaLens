
using KafkaLens.Core.DataAccess;
using KafkaLens.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System.IO;

namespace KafkaLens;

public sealed partial class App1 : Application
{
    private IHost Host { get; } = BuildAppHost();

	private static IHost BuildAppHost()
	{
		var host = UnoHost
				.CreateDefaultBuilder()
#if DEBUG
				// Switch to Development environment when running in DEBUG
				.UseEnvironment(Environments.Development)
#endif

				.UseConfiguration(configure: configBuilder =>
					configBuilder
						.EmbeddedSource<App1>()
						.Section<AppConfig>()
				)

				// Enable localization (see appsettings.json for supported languages)
				.UseLocalization()

				// Register Json serializers (ISerializer and ISerializer)
				.UseSerialization()

				// Register services for the application
				.ConfigureServices(services =>
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    string appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
					string kafkaLensDataDir = Path.Combine(appDataDir, "KafkaLens");
					string dbPath = Path.Combine(kafkaLensDataDir, "KafkaDB.db");
                    services.AddSingleton<ISettingsService, SettingsService>();
                    services.AddDbContext<KafkaContext>(opt => opt.UseSqlite($"Data Source={dbPath};"));
                    services.AddSingleton<IClusterService, LocalClusterService>();
                    services.AddSingleton<ConsumerFactory>();
                    services.AddSingleton<MainViewModel>();
                    services.AddLogging();
                })


				// Enable navigation, including registering views and viewmodels
				.UseNavigation(ReactiveViewModelMappings.ViewModelMappings, RegisterRoutes)

				// Add navigation support for toolkit controls such as TabBar and NavigationView
				.UseToolkitNavigation()

				.Build(enableUnoLogging: true);

        ConfigureLogging();

		return host;
    }

    private static void ConfigureLogging()
    {
        using var log = new LoggerConfiguration()
            .WriteTo.Console(outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u4}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();
        Log.Logger = log;
        Log.Information("The global logger has been configured");
    }

    private static void RegisterRoutes(IViewRegistry views, IRouteRegistry routes)
	{
		views.Register(
			new ViewMap<ShellControl, ShellViewModel>(),
			new ViewMap<MainPage, MainViewModel>()
			);

		routes
			.Register(
				new RouteMap("", View: views.FindByViewModel<ShellViewModel>(),
						Nested: new RouteMap[]
						{
										new RouteMap("Main", View: views.FindByViewModel<MainViewModel>())
						}));
	}
}