using Avalonia.Headless.XUnit;
using KafkaLens.ViewModels.Services;

namespace KafkaLens.ViewModels.Tests;

public class AppLogServiceTests
{
    [AvaloniaFact]
    public void LogInfo_ShouldAddEntryAndUpdateLatestEntry()
    {
        var service = new AppLogService();

        service.LogInfo("Loaded clusters", "Startup");

        Assert.Single(service.Entries);
        Assert.NotNull(service.LatestEntry);
        Assert.Equal(AppLogLevel.Info, service.LatestEntry.Level);
        Assert.Equal("Loaded clusters", service.LatestEntry.Message);
        Assert.Equal("Startup", service.LatestEntry.Source);
    }

    [AvaloniaFact]
    public void Clear_ShouldRemoveEntriesAndLatestEntry()
    {
        var service = new AppLogService();
        service.LogWarning("Connection failed");

        service.Clear();

        Assert.Empty(service.Entries);
        Assert.Null(service.LatestEntry);
    }

    [AvaloniaFact]
    public void LogInfo_WhenHistoryLimitExceeded_ShouldRemoveOldestEntries()
    {
        var service = new AppLogService(maxEntries: 2);

        service.LogInfo("First");
        service.LogInfo("Second");
        service.LogInfo("Third");

        Assert.Equal(2, service.Entries.Count);
        Assert.Equal("Second", service.Entries[0].Message);
        Assert.Equal("Third", service.Entries[1].Message);
        Assert.Equal("Third", service.LatestEntry?.Message);
    }

    [AvaloniaFact]
    public void LogError_ShouldNotIncludeExceptionStackTraceWhenCallerUsesFriendlyMessage()
    {
        var service = new AppLogService();

        service.LogError("Could not connect to Orders: broker unavailable");

        Assert.Single(service.Entries);
        Assert.DoesNotContain(" at ", service.Entries[0].Message);
        Assert.DoesNotContain(nameof(Exception), service.Entries[0].Message);
    }
}
