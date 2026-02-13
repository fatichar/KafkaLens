using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(KafkaLens.ViewModels.Tests.TestAppBuilder))]

namespace KafkaLens.ViewModels.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<TestApp>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
