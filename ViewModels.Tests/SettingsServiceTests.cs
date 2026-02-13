using System.IO;

namespace KafkaLens.ViewModels.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string _tempFilePath;

    public SettingsServiceTests()
    {
        _tempFilePath = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid()}.json");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFilePath))
        {
            File.Delete(_tempFilePath);
        }
    }

    [Fact]
    public void GetValue_WhenKeyDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        var service = new SettingsService(_tempFilePath);

        // Act
        var result = service.GetValue("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SetValue_ShouldStoreAndRetrieve()
    {
        // Arrange
        var service = new SettingsService(_tempFilePath);

        // Act
        service.SetValue("Theme", "Dark");
        var result = service.GetValue("Theme");

        // Assert
        Assert.Equal("Dark", result);
    }

    [Fact]
    public void SetValue_ShouldOverwriteExistingValue()
    {
        // Arrange
        var service = new SettingsService(_tempFilePath);
        service.SetValue("Theme", "Dark");

        // Act
        service.SetValue("Theme", "Light");
        var result = service.GetValue("Theme");

        // Assert
        Assert.Equal("Light", result);
    }

    [Fact]
    public void SetValue_ShouldPersistToFile()
    {
        // Arrange
        var service1 = new SettingsService(_tempFilePath);
        service1.SetValue("Theme", "Dark");

        // Act
        var service2 = new SettingsService(_tempFilePath);
        var result = service2.GetValue("Theme");

        // Assert
        Assert.Equal("Dark", result);
    }

    [Fact]
    public void Constructor_WithNonExistentFile_ShouldNotThrow()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.json");

        // Act
        var service = new SettingsService(nonExistentPath);

        // Assert
        Assert.Null(service.GetValue("anything"));
    }

    [Fact]
    public void SetValue_MultipleKeys_ShouldStoreAll()
    {
        // Arrange
        var service = new SettingsService(_tempFilePath);

        // Act
        service.SetValue("Theme", "Dark");
        service.SetValue("FontSize", "14");
        service.SetValue("Language", "en");

        // Assert
        Assert.Equal("Dark", service.GetValue("Theme"));
        Assert.Equal("14", service.GetValue("FontSize"));
        Assert.Equal("en", service.GetValue("Language"));
    }
}
