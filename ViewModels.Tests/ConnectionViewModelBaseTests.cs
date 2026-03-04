using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels.Tests;

public class ConnectionViewModelBaseTests
{
    [Fact]
    public void StatusChanged_WhenConnected_SetsConnectedStatus()
    {
        // Arrange
        var viewModel = new TestConnectionViewModelBase();
        
        // Act
        viewModel.Status = ConnectionState.Connected;
        
        // Assert
        Assert.Equal(ConnectionState.Connected, viewModel.Status);
        Assert.Equal("Connected", viewModel.ConnectionStatus);
        Assert.Equal("Green", viewModel.StatusColor);
        Assert.False(viewModel.IsChecking);
    }

    [Fact]
    public void StatusChanged_WhenFailed_SetsDisconnectedStatus()
    {
        // Arrange
        var viewModel = new TestConnectionViewModelBase();
        
        // Act
        viewModel.Status = ConnectionState.Failed;
        
        // Assert
        Assert.Equal(ConnectionState.Failed, viewModel.Status);
        Assert.Equal("Disconnected", viewModel.ConnectionStatus);
        Assert.Equal("Red", viewModel.StatusColor);
        Assert.False(viewModel.IsChecking);
    }

    [Fact]
    public void StatusChanged_WhenChecking_SetsCheckingStatus()
    {
        // Arrange
        var viewModel = new TestConnectionViewModelBase();
        
        // Act
        viewModel.Status = ConnectionState.Checking;
        
        // Assert
        Assert.Equal(ConnectionState.Checking, viewModel.Status);
        Assert.Equal("Checking...", viewModel.ConnectionStatus);
        Assert.True(viewModel.IsChecking);
    }

    [Fact]
    public void InitialState_ShouldHaveUnknownStatus()
    {
        // Arrange & Act
        var viewModel = new TestConnectionViewModelBase();
        
        // Assert
        Assert.Equal(ConnectionState.Unknown, viewModel.Status);
        Assert.Equal("Unknown", viewModel.ConnectionStatus);
        Assert.Equal("Gray", viewModel.StatusColor);
        Assert.False(viewModel.IsChecking);
    }

    [Fact]
    public void StatusChanged_ShouldRaisePropertyChangedForAllProperties()
    {
        // Arrange
        var viewModel = new TestConnectionViewModelBase();
        var changedProperties = new List<string>();
        viewModel.PropertyChanged += (_, args) => changedProperties.Add(args.PropertyName!);
        
        // Act
        viewModel.Status = ConnectionState.Checking;
        viewModel.Status = ConnectionState.Connected;
        
        // Assert
        Assert.Contains(nameof(ConnectionViewModelBase.Status), changedProperties);
        Assert.Contains(nameof(ConnectionViewModelBase.ConnectionStatus), changedProperties);
        Assert.Contains(nameof(ConnectionViewModelBase.StatusColor), changedProperties);
        Assert.Contains(nameof(ConnectionViewModelBase.IsChecking), changedProperties);
    }
}

public class TestConnectionViewModelBase : ConnectionViewModelBase
{
}
