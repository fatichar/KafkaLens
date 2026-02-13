namespace KafkaLens.ViewModels.Tests;

public class MessageViewOptionsTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var options = new MessageViewOptions();
        
        // Assert
        Assert.Equal("", options.FormatterName);
        Assert.False(options.UseObjectFilter);
        Assert.Equal("", options.FilterText);
    }

    [Fact]
    public void Properties_ShouldSetAndGetCorrectly()
    {
        // Arrange
        var options = new MessageViewOptions
        {
            FormatterName = "JSON",
            UseObjectFilter = true,
            FilterText = "test filter"
        };
        
        // Assert
        Assert.Equal("JSON", options.FormatterName);
        Assert.True(options.UseObjectFilter);
        Assert.Equal("test filter", options.FilterText);
    }
}
