namespace KafkaLens.ViewModels.Tests;

public class ConnectionViewModelBaseTests
{
    [Fact]
    public void OnIsConnectedChanged_WhenTrue_SetsConnectedStatus()
    {
        // Arrange
        var viewModel = new TestConnectionViewModelBase();
        
        // Act
        viewModel.SetIsConnected(true);
        
        // Assert
        Assert.True(viewModel.IsConnected);
        Assert.Equal("Connected", viewModel.ConnectionStatus);
        Assert.Equal("Green", viewModel.StatusColor);
    }

    [Fact]
    public void OnIsConnectedChanged_WhenFalse_SetsDisconnectedStatus()
    {
        // Arrange
        var viewModel = new TestConnectionViewModelBase();
        
        // Act
        viewModel.SetIsConnected(false);
        
        // Assert
        Assert.False(viewModel.IsConnected);
        Assert.Equal("Disconnected", viewModel.ConnectionStatus);
        Assert.Equal("Red", viewModel.StatusColor);
    }

    [Fact]
    public void OnIsConnectedChanged_WhenNull_SetsUnknownStatus()
    {
        // Arrange
        var viewModel = new TestConnectionViewModelBase();
        
        // Act
        viewModel.SetIsConnected(null);
        
        // Assert
        Assert.Null(viewModel.IsConnected);
        Assert.Equal("Unknown", viewModel.ConnectionStatus);
        Assert.Equal("Gray", viewModel.StatusColor);
    }

    [Fact]
    public void InitialState_ShouldHaveUnknownStatus()
    {
        // Arrange & Act
        var viewModel = new TestConnectionViewModelBase();
        
        // Assert
        Assert.Null(viewModel.IsConnected);
        Assert.Equal("Unknown", viewModel.ConnectionStatus);
        Assert.Equal("Gray", viewModel.StatusColor);
    }

    [Fact]
    public void OnIsConnectedChanged_ShouldRaisePropertyChangedForAllProperties()
    {
        // Arrange
        var viewModel = new TestConnectionViewModelBase();
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName!);
        
        // Act
        viewModel.SetIsConnected(true);
        
        // Assert
        Assert.Contains(nameof(ConnectionViewModelBase.IsConnected), changedProperties);
        Assert.Contains(nameof(ConnectionViewModelBase.ConnectionStatus), changedProperties);
        Assert.Contains(nameof(ConnectionViewModelBase.StatusColor), changedProperties);
    }
}

public class TestConnectionViewModelBase : ConnectionViewModelBase
{
    public void SetIsConnected(bool? value)
    {
        IsConnected = value;
    }
}
