namespace KafkaLens.ViewModels.Tests.Messages;

using KafkaLens.ViewModels.Messages;

public class ThemeChangedMessageTests
{
    [Fact]
    public void Constructor_ShouldSetTheme()
    {
        // Arrange
        var theme = "Dark";
        
        // Act
        var message = new ThemeChangedMessage(theme);
        
        // Assert
        Assert.Equal(theme, message.Value);
    }

    [Fact]
    public void ShouldInheritFromValueChangedMessage()
    {
        // Arrange
        var theme = "Light";
        var message = new ThemeChangedMessage(theme);
        
        // Act & Assert
        message.Should().BeAssignableTo<CommunityToolkit.Mvvm.Messaging.Messages.ValueChangedMessage<string>>();
    }

    [Theory]
    [InlineData("Dark")]
    [InlineData("Light")]
    [InlineData("System")]
    [InlineData("")]
    public void Constructor_ShouldHandleVariousThemes(string theme)
    {
        // Act
        var message = new ThemeChangedMessage(theme);
        
        // Assert
        Assert.Equal(theme, message.Value);
    }
}
