namespace KafkaLens.ViewModels.Tests;

public class ClientInfoViewModelTests
{
    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Arrange
        var clientInfo = new ClientInfo("test-id", "Test Client", "localhost:9092", "gRPC");
        
        // Act
        var viewModel = new ClientInfoViewModel(clientInfo);
        
        // Assert
        Assert.Equal(clientInfo, viewModel.Info);
        Assert.Equal("Test Client", viewModel.Name);
        Assert.Equal("localhost:9092", viewModel.Address);
        Assert.Equal("test-id", viewModel.Id);
        Assert.Equal("gRPC", viewModel.Protocol);
    }

    [Fact]
    public void Properties_ShouldReturnCorrectValuesFromInfo()
    {
        // Arrange
        var clientInfo = new ClientInfo("client-123", "My Client", "192.168.1.100:8080", "REST");
        var viewModel = new ClientInfoViewModel(clientInfo);
        
        // Act & Assert
        Assert.Equal(clientInfo.Name, viewModel.Name);
        Assert.Equal(clientInfo.Address, viewModel.Address);
        Assert.Equal(clientInfo.Id, viewModel.Id);
        Assert.Equal(clientInfo.Protocol, viewModel.Protocol);
    }
}
