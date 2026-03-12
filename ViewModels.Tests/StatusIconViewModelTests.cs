namespace KafkaLens.ViewModels.Tests;

public class StatusIconViewModelTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithGray()
    {
        // Arrange & Act
        var viewModel = new StatusIconViewModel();

        // Assert
        Assert.Equal("Gray", viewModel.Color);
    }

    [Theory]
    [InlineData(ConnectionState.Connected, "Green")]
    [InlineData(ConnectionState.Failed, "Red")]
    [InlineData(ConnectionState.Unknown, "Gray")]
    public void Status_ShouldSetCorrectColor(ConnectionState status, string expectedColor)
    {
        // Arrange
        var viewModel = new StatusIconViewModel();

        // Act
        viewModel.Status = status;

        // Assert
        Assert.Equal(expectedColor, viewModel.Color);
    }

    [Fact]
    public void Status_Checking_ShouldSetIsLoading()
    {
        // Arrange
        var viewModel = new StatusIconViewModel();

        // Act
        viewModel.Status = ConnectionState.Checking;

        // Assert
        Assert.True(viewModel.IsLoading);
        Assert.Equal("Gray", viewModel.Color);
    }

    [Fact]
    public void Status_ShouldRaisePropertyChanged()
    {
        // Arrange
        var viewModel = new StatusIconViewModel();
        var propertyChangedRaised = false;
        viewModel.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(StatusIconViewModel.Status))
                propertyChangedRaised = true;
        };

        // Act
        viewModel.Status = ConnectionState.Connected;

        // Assert
        Assert.True(propertyChangedRaised);
    }
}