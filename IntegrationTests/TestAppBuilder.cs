using Avalonia;
using Avalonia.Headless;
using AvaloniaApp;
using Avalonia.Headless.XUnit;

[assembly: AvaloniaTestApplication(typeof(IntegrationTests.TestAppBuilder))]

namespace IntegrationTests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
