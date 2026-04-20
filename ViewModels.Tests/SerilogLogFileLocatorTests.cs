using System.IO;
using AvaloniaApp.Services;

namespace KafkaLens.ViewModels.Tests;

public class SerilogLogFileLocatorTests : IDisposable
{
    private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public SerilogLogFileLocatorTests()
    {
        Directory.CreateDirectory(tempDirectory);
    }

    [Fact]
    public void FindLatestLogFile_WhenDirectoryMissing_ShouldReturnNull()
    {
        var locator = new SerilogLogFileLocator(Path.Combine(tempDirectory, "missing", "log.txt"));

        var result = locator.FindLatestLogFile();

        Assert.Null(result);
    }

    [Fact]
    public void FindLatestLogFile_ShouldReturnNewestMatchingSerilogFile()
    {
        var older = Path.Combine(tempDirectory, "log20260419.txt");
        var newer = Path.Combine(tempDirectory, "log20260420.txt");
        var unrelated = Path.Combine(tempDirectory, "other.txt");
        File.WriteAllText(older, "older");
        File.WriteAllText(newer, "newer");
        File.WriteAllText(unrelated, "other");
        File.SetLastWriteTimeUtc(older, new DateTime(2026, 4, 19, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(newer, new DateTime(2026, 4, 20, 10, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(unrelated, new DateTime(2026, 4, 21, 10, 0, 0, DateTimeKind.Utc));

        var locator = new SerilogLogFileLocator(Path.Combine(tempDirectory, "log.txt"));

        var result = locator.FindLatestLogFile();

        Assert.Equal(newer, result);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDirectory))
            Directory.Delete(tempDirectory, recursive: true);
    }
}
