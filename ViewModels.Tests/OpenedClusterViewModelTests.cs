using KafkaLens.ViewModels.Services;

namespace KafkaLens.ViewModels.Tests;

public class OpenedClusterViewModelTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Auto")]
    [InlineData("Unknown")]
    public void NormalizeFormatterName_WhenNullOrWhitespaceOrUnknown_ShouldReturnUnknown(string? formatterName)
    {
        // Arrange
        var service = new FormatterService();
        var allowedNames = new List<string> { "Unknown", "Text", "JSON" };

        // Act
        var result = service.NormalizeFormatterName(formatterName, allowedNames);

        // Assert
        Assert.Equal("Unknown", result);
    }

    [Fact]
    public void NormalizeFormatterName_WhenAllowed_ShouldReturnSameName()
    {
        // Arrange
        var service = new FormatterService();
        var allowedNames = new List<string> { "Unknown", "Text", "JSON" };

        // Act
        var result = service.NormalizeFormatterName("JSON", allowedNames);

        // Assert
        Assert.Equal("JSON", result);
    }

    [Fact]
    public void NormalizeFormatterName_WhenNotAllowed_ShouldReturnAuto()
    {
        // Arrange
        var service = new FormatterService();
        var allowedNames = new List<string> { "Unknown", "Text", "JSON" };

        // Act
        var result = service.NormalizeFormatterName("XML", allowedNames);

        // Assert
        Assert.Equal("Unknown", result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Auto")]
    [InlineData("Unknown")]
    public void CanApplyFormatterToLoadedMessages_WhenNullOrWhitespaceOrUnknown_ShouldReturnFalse(string? formatterName)
    {
        // Arrange
        var service = new FormatterService();
        var allowedNames = new List<string> { "Unknown", "Text", "JSON" };

        // Act
        var result = service.CanApplyFormatterToLoadedMessages(formatterName, allowedNames);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void CanApplyFormatterToLoadedMessages_WhenValidAndAllowed_ShouldReturnTrue()
    {
        // Arrange
        var service = new FormatterService();
        var allowedNames = new List<string> { "Unknown", "Text", "JSON" };

        // Act
        var result = service.CanApplyFormatterToLoadedMessages("JSON", allowedNames);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void CanApplyFormatterToLoadedMessages_WhenValidButNotAllowed_ShouldReturnFalse()
    {
        // Arrange
        var service = new FormatterService();
        var allowedNames = new List<string> { "Unknown", "Text", "JSON" };

        // Act
        var result = service.CanApplyFormatterToLoadedMessages("XML", allowedNames);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void BuildFormatterNames_WhenConfiguredListExists_ShouldAppendNewPluginFormatterNames()
    {
        // Arrange
        var service = new FormatterService();
        var allowedNames = new List<string> { "Json", "Text", "TestPluginFormatter" };

        // Act
        var result = service.BuildFormatterNames("[\"Text\"]", allowedNames);

        // Assert
        Assert.Equal(new List<string> { "Unknown", "Text", "TestPluginFormatter" }, result);
    }
}
