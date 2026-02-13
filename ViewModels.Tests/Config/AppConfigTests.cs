namespace KafkaLens.ViewModels.Tests.Config;

using KafkaLens.ViewModels.Config;

public class AppConfigTests
{
    [Fact]
    public void Constructor_ShouldInitializeWithDefaults()
    {
        // Arrange & Act
        var config = new AppConfig();
        
        // Assert
        Assert.Null(config.Title);
        Assert.Equal("cluster_info.json", config.ClusterInfoFilePath);
        Assert.Equal("client_info.json", config.ClientInfoFilePath);
        Assert.Equal(100, config.ClusterRefreshIntervalSeconds);
    }

    [Fact]
    public void Properties_ShouldSetAndGetCorrectly()
    {
        // Arrange
        var config = new AppConfig
        {
            Title = "KafkaLens",
            ClusterInfoFilePath = "custom_clusters.json",
            ClientInfoFilePath = "custom_clients.json",
            ClusterRefreshIntervalSeconds = 300
        };
        
        // Assert
        Assert.Equal("KafkaLens", config.Title);
        Assert.Equal("custom_clusters.json", config.ClusterInfoFilePath);
        Assert.Equal("custom_clients.json", config.ClientInfoFilePath);
        Assert.Equal(300, config.ClusterRefreshIntervalSeconds);
    }

    [Fact]
    public void ShouldBeRecordType()
    {
        // Arrange
        var config1 = new AppConfig { Title = "Test" };
        var config2 = config1 with { Title = "Test" };
        var config3 = config1 with { Title = "Different" };
        
        // Assert
        Assert.Equal(config1, config2);
        Assert.NotEqual(config1, config3);
    }
}
