namespace KafkaLens.ViewModels.Tests;

public class ClusterInfoViewModelTests
{
    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Arrange
        var clusterInfo = new ClusterInfo("cluster-id", "Test Cluster", "localhost:9092");
        
        // Act
        var viewModel = new ClusterInfoViewModel(clusterInfo);
        
        // Assert
        Assert.Equal(clusterInfo, viewModel.Info);
        Assert.Equal("Test Cluster", viewModel.Name);
        Assert.Equal("localhost:9092", viewModel.Address);
        Assert.Equal("cluster-id", viewModel.Id);
    }

    [Fact]
    public void Properties_ShouldReturnCorrectValuesFromInfo()
    {
        // Arrange
        var clusterInfo = new ClusterInfo("cluster-456", "Production Cluster", "kafka.example.com:9092");
        var viewModel = new ClusterInfoViewModel(clusterInfo);
        
        // Act & Assert
        Assert.Equal(clusterInfo.Name, viewModel.Name);
        Assert.Equal(clusterInfo.Address, viewModel.Address);
        Assert.Equal(clusterInfo.Id, viewModel.Id);
    }
}
