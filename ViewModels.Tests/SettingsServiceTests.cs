using System.IO;
using KafkaLens.Shared.Models;
using Newtonsoft.Json.Linq;

namespace KafkaLens.ViewModels.Tests;

public class SettingsServiceTests : IDisposable
{
    private readonly string tempFilePath = Path.Combine(Path.GetTempPath(), $"settings_test_{Guid.NewGuid()}.json");

    public void Dispose()
    {
        if (File.Exists(tempFilePath))
        {
            File.Delete(tempFilePath);
        }
    }

    [Fact]
    public void GetValue_WhenKeyDoesNotExist_ShouldReturnNull()
    {
        // Arrange
        var service = new SettingsService(tempFilePath);

        // Act
        var result = service.GetValue("nonexistent");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void SetValue_ShouldStoreAndRetrieve()
    {
        // Arrange
        var service = new SettingsService(tempFilePath);

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
        var service = new SettingsService(tempFilePath);
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
        var service1 = new SettingsService(tempFilePath);
        service1.SetValue("Theme", "Dark");

        // Act
        var service2 = new SettingsService(tempFilePath);
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
    public void Constructor_WithNonExistentFile_ShouldCreateSettingsFileWithDefaults()
    {
        // Arrange
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"nonexistent_{Guid.NewGuid()}.json");

        try
        {
            // Act
            var service = new SettingsService(nonExistentPath);

            // Assert
            Assert.True(File.Exists(nonExistentPath));

            var kafkaConfig = service.GetKafkaConfig();
            var browserConfig = service.GetBrowserConfig();
            var pluginSettings = service.GetPluginSettings();

            Assert.Equal(new KafkaConfig().QueryWatermarkTimeoutMs, kafkaConfig.QueryWatermarkTimeoutMs);
            Assert.Equal(new BrowserConfig().DefaultFetchCount, browserConfig.DefaultFetchCount);
            Assert.Equal(new BrowserConfig().FetchCounts, browserConfig.FetchCounts);
            Assert.Equal(["https://fatichar.github.io/kafkalens-plugin-index/plugins.json"], pluginSettings.Repositories);
            Assert.Empty(pluginSettings.PluginStates);
            Assert.Equal("System", service.GetValue("Theme"));
            Assert.Equal("true", service.GetValue("AutoCheckForUpdates"));
            Assert.Equal("[]", service.GetValue("HiddenKeyFormatters"));
            Assert.Equal("[]", service.GetValue("HiddenValueFormatters"));
        }
        finally
        {
            if (File.Exists(nonExistentPath))
            {
                File.Delete(nonExistentPath);
            }
        }
    }

    [Fact]
    public void Constructor_WithMissingConfigKeys_ShouldBackfillDefaultsAndPreserveExistingValues()
    {
        // Arrange
        File.WriteAllText(tempFilePath, """
        {
          "AutoCheckForUpdates": "false",
          "KafkaConfig": {
            "GroupId": "custom-group"
          },
          "BrowserConfig": {
            "FontSize": 18
          },
          "PluginSettings": {
            "Repositories": ["https://example.test/index.json"]
          },
          "HiddenKeyFormatters": "[\"Bytes\"]"
        }
        """);

        // Act
        var service = new SettingsService(tempFilePath);
        var kafkaConfig = service.GetKafkaConfig();
        var browserConfig = service.GetBrowserConfig();
        var pluginSettings = service.GetPluginSettings();
        var persisted = JObject.Parse(File.ReadAllText(tempFilePath));

        // Assert
        Assert.Equal("custom-group", kafkaConfig.GroupId);
        Assert.Equal(new KafkaConfig().QueryWatermarkTimeoutMs, kafkaConfig.QueryWatermarkTimeoutMs);

        Assert.Equal(18, browserConfig.FontSize);
        Assert.Equal(new BrowserConfig().DefaultFetchCount, browserConfig.DefaultFetchCount);
        Assert.Equal(new BrowserConfig().FetchCounts, browserConfig.FetchCounts);

        Assert.Equal(["https://example.test/index.json"], pluginSettings.Repositories);
        Assert.Empty(pluginSettings.PluginStates);
        Assert.Equal("false", service.GetValue("AutoCheckForUpdates"));
        Assert.Equal("[\"Bytes\"]", service.GetValue("HiddenKeyFormatters"));
        Assert.Equal("[]", service.GetValue("HiddenValueFormatters"));
        Assert.Equal("System", service.GetValue("Theme"));

        Assert.NotNull(persisted["Theme"]);
        Assert.NotNull(persisted["AutoCheckForUpdates"]);
        Assert.NotNull(persisted["HiddenKeyFormatters"]);
        Assert.NotNull(persisted["HiddenValueFormatters"]);
        Assert.NotNull(persisted["KafkaConfig"]?["QueryWatermarkTimeoutMs"]);
        Assert.NotNull(persisted["BrowserConfig"]?["DefaultFetchCount"]);
        Assert.NotNull(persisted["BrowserConfig"]?["FetchCounts"]);
        Assert.NotNull(persisted["PluginSettings"]?["PluginStates"]);
    }

    [Fact]
    public void Constructor_WithEmptyPluginRepositories_ShouldSeedDefaultRepository()
    {
        // Arrange
        File.WriteAllText(tempFilePath, """
        {
          "PluginSettings": {
            "Repositories": [],
            "PluginStates": {}
          }
        }
        """);

        // Act
        var service = new SettingsService(tempFilePath);
        var pluginSettings = service.GetPluginSettings();
        var persisted = JObject.Parse(File.ReadAllText(tempFilePath));

        // Assert
        Assert.Equal(["https://fatichar.github.io/kafkalens-plugin-index/plugins.json"], pluginSettings.Repositories);
        Assert.Equal(["https://fatichar.github.io/kafkalens-plugin-index/plugins.json"], persisted["PluginSettings"]?["Repositories"]?.Values<string>());
    }

    [Fact]
    public void SetValue_MultipleKeys_ShouldStoreAll()
    {
        // Arrange
        var service = new SettingsService(tempFilePath);

        // Act
        service.SetValue("Theme", "Dark");
        service.SetValue("FontSize", "14");
        service.SetValue("Language", "en");

        // Assert
        Assert.Equal("Dark", service.GetValue("Theme"));
        Assert.Equal("14", service.GetValue("FontSize"));
        Assert.Equal("en", service.GetValue("Language"));
    }

    [Fact]
    public void GetValue_WhenValueIsArray_ShouldReturnCommaSeparated()
    {
        // Arrange
        File.WriteAllText(tempFilePath, """
        {
          "KeyFormatterNames": ["Text", "Int32", "UInt32"]
        }
        """);
        var service = new SettingsService(tempFilePath);

        // Act
        var result = service.GetValue("KeyFormatterNames");

        // Assert
        Assert.Equal("Text,Int32,UInt32", result);
    }
}
