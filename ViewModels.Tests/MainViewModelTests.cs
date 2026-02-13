using KafkaLens.Shared;

namespace KafkaLens.ViewModels.Tests;

public class MainViewModelTests
{
    public MainViewModelTests()
    {
        OpenedClusterViewModel.FormatterFactory = FormatterFactory.Instance;
    }

    private OpenedClusterViewModel CreateOpenedCluster(string name)
    {
        var settingsService = Substitute.For<ISettingsService>();
        var topicSettingsService = Substitute.For<ITopicSettingsService>();
        var cluster = new KafkaCluster("1", "MyCluster", "localhost");
        var client = Substitute.For<IKafkaLensClient>();
        var clusterVm = new ClusterViewModel(cluster, client);
        return new OpenedClusterViewModel(settingsService, topicSettingsService, clusterVm, name);
    }

    [Fact]
    public void GenerateNewName_WithOneExisting_ShouldReturnSuffix1()
    {
        // Arrange
        var clusterName = "MyCluster";
        var alreadyOpened = new List<OpenedClusterViewModel> { CreateOpenedCluster(clusterName) };

        // Act
        var result = MainViewModel.GenerateNewName(clusterName, alreadyOpened);

        // Assert
        Assert.Equal("MyCluster (1)", result);
    }

    [Fact]
    public void GenerateNewName_WithGap_ShouldFillSmallestAvailable()
    {
        // Arrange
        var clusterName = "MyCluster";
        var alreadyOpened = new List<OpenedClusterViewModel>
        {
            CreateOpenedCluster(clusterName),
            CreateOpenedCluster("MyCluster (2)")
        };

        // Act
        var result = MainViewModel.GenerateNewName(clusterName, alreadyOpened);

        // Assert
        Assert.Equal("MyCluster (1)", result);
    }

    [Fact]
    public void GenerateNewName_WithConsecutive_ShouldReturnNext()
    {
        // Arrange
        var clusterName = "MyCluster";
        var alreadyOpened = new List<OpenedClusterViewModel>
        {
            CreateOpenedCluster(clusterName),
            CreateOpenedCluster("MyCluster (1)")
        };

        // Act
        var result = MainViewModel.GenerateNewName(clusterName, alreadyOpened);

        // Assert
        Assert.Equal("MyCluster (2)", result);
    }
}
