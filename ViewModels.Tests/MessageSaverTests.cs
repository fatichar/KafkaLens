using System.IO;
using System.Text;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels.Services;
using NSubstitute;

namespace KafkaLens.ViewModels.Tests;

public sealed class MessageSaverTests : IDisposable
{
    private readonly string saveDirectory = Path.Combine(
        Path.GetTempPath(),
        $"kafkalens-save-test-{Guid.NewGuid():N}");

    [Fact]
    public async Task SaveAsync_UsesConfiguredDirectoryAndReturnsSaveResult()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            SavedMessagesDirectory = saveDirectory
        });
        var message = new Message(
            1640995200000,
            new Dictionary<string, byte[]>(),
            null,
            Encoding.UTF8.GetBytes("body"))
        {
            Partition = 2,
            Offset = 42
        };
        var messageViewModel = new MessageViewModel(message, "Text", "Text")
        {
            Topic = "orders"
        };
        var saver = new MessageSaver(Substitute.For<IClientFactory>(), settingsService);

        // Act
        var result = await saver.SaveAsync([messageViewModel], "cluster", formatted: false);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(saveDirectory, result.Directory);
        Assert.Equal(1, result.Count);
        Assert.True(File.Exists(Path.Combine(saveDirectory, "cluster", "orders", "2", "42.klm")));
    }

    [Fact]
    public async Task SaveAsync_WhenNoMessages_ShouldReturnNoResult()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            SavedMessagesDirectory = saveDirectory
        });
        var saver = new MessageSaver(Substitute.For<IClientFactory>(), settingsService);

        // Act
        var result = await saver.SaveAsync([], "cluster", formatted: false);

        // Assert
        Assert.Null(result);
        Assert.False(Directory.Exists(saveDirectory));
    }

    public void Dispose()
    {
        if (Directory.Exists(saveDirectory))
            Directory.Delete(saveDirectory, recursive: true);
    }
}
