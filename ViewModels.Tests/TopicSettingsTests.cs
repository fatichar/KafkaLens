namespace KafkaLens.ViewModels.Tests;

public class TopicSettingsTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var settings = new TopicSettings();
        
        // Assert
        Assert.Equal("Auto", settings.KeyFormatter);
        Assert.Equal("Auto", settings.ValueFormatter);
    }

    [Fact]
    public void Properties_ShouldSetAndGetCorrectly()
    {
        // Arrange
        var settings = new TopicSettings
        {
            KeyFormatter = "JSON",
            ValueFormatter = "Text"
        };
        
        // Assert
        Assert.Equal("JSON", settings.KeyFormatter);
        Assert.Equal("Text", settings.ValueFormatter);
    }
}
