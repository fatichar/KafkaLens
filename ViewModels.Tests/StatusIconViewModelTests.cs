namespace KafkaLens.ViewModels.Tests;

public class StatusIconViewModelTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithEmptyColor()
    {
        // Arrange & Act
        var viewModel = new StatusIconViewModel();
        
        // Assert
        Assert.Equal("", viewModel.Color);
    }

    [Theory]
    [InlineData("Red")]
    [InlineData("Green")]
    [InlineData("Blue")]
    [InlineData("")]
    public void Color_ShouldSetAndGetCorrectly(string color)
    {
        // Arrange
        var viewModel = new StatusIconViewModel();
        
        // Act
        viewModel.Color = color;
        
        // Assert
        Assert.Equal(color, viewModel.Color);
    }

    [Fact]
    public void Color_ShouldRaisePropertyChanged()
    {
        // Arrange
        var viewModel = new StatusIconViewModel();
        var propertyChangedRaised = false;
        viewModel.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(StatusIconViewModel.Color))
                propertyChangedRaised = true;
        };
        
        // Act
        viewModel.Color = "Yellow";
        
        // Assert
        Assert.True(propertyChangedRaised);
    }
}
