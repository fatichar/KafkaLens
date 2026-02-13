using KafkaLens.Shared;

namespace KafkaLens.ViewModels.Tests;

public class ClusterViewModelTests
{
    private readonly IFixture _fixture;
    private readonly IKafkaLensClient _mockClient;
    private readonly KafkaCluster _cluster;

    public ClusterViewModelTests()
    {
        _fixture = new Fixture();
        _mockClient = Substitute.For<IKafkaLensClient>();
        _cluster = _fixture.Create<KafkaCluster>();
    }

    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Act
        var viewModel = new ClusterViewModel(_cluster, _mockClient);

        // Assert
        Assert.Equal(_mockClient, viewModel.Client);
        Assert.Equal(_cluster.Id, viewModel.Id);
        Assert.Equal(_cluster.Name, viewModel.Name);
        Assert.Equal(_cluster.Address, viewModel.Address);
        Assert.Equal(_cluster.IsConnected, viewModel.IsConnected);
        Assert.NotNull(viewModel.Topics);
        Assert.NotNull(viewModel.LoadTopicsCommand);
    }

    [Fact]
    public async Task CheckConnectionAsync_ShouldUpdateIsConnected()
    {
        // Arrange
        var viewModel = new ClusterViewModel(_cluster, _mockClient);
        _mockClient.ValidateConnectionAsync(_cluster.Address).Returns(Task.FromResult(true));

        // Act
        await viewModel.CheckConnectionAsync();

        // Assert
        Assert.True(viewModel.IsConnected);
        await _mockClient.Received(1).ValidateConnectionAsync(_cluster.Address);
    }

    [Fact]
    public async Task LoadTopicsAsync_ShouldLoadTopicsSuccessfully()
    {
        // Arrange
        var viewModel = new ClusterViewModel(_cluster, _mockClient);
        var topics = _fixture.CreateMany<Topic>().ToList();
        _mockClient.GetTopicsAsync(_cluster.Id).Returns((IList<Topic>)topics);

        // Act
        await viewModel.LoadTopicsCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(topics.Count, viewModel.Topics.Count);
        viewModel.Topics.Should().BeEquivalentTo(topics);
        Assert.True(viewModel.IsConnected);
        await _mockClient.Received(1).GetTopicsAsync(_cluster.Id);
    }

    [Fact]
    public async Task LoadTopicsAsync_ShouldHandleException()
    {
        // Arrange
        var viewModel = new ClusterViewModel(_cluster, _mockClient);
        _mockClient.GetTopicsAsync(_cluster.Id).Returns(Task.FromException<IList<Topic>>(new Exception("Test error")));

        // Act
        await viewModel.LoadTopicsCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(viewModel.Topics);
        Assert.False(viewModel.IsConnected);
    }

    [Fact]
    public async Task LoadTopicsAsync_ShouldClearExistingTopics()
    {
        // Arrange
        var viewModel = new ClusterViewModel(_cluster, _mockClient);
        viewModel.Topics.Add(_fixture.Create<Topic>());
        var newTopics = _fixture.CreateMany<Topic>().ToList();
        _mockClient.GetTopicsAsync(_cluster.Id).Returns((IList<Topic>)newTopics);

        // Act
        await viewModel.LoadTopicsCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(newTopics.Count, viewModel.Topics.Count);
        viewModel.Topics.Should().BeEquivalentTo(newTopics);
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenReturnsFalse_ShouldSetIsConnectedToFalse()
    {
        // Arrange
        var viewModel = new ClusterViewModel(_cluster, _mockClient);
        _mockClient.ValidateConnectionAsync(_cluster.Address).Returns(Task.FromResult(false));

        // Act
        await viewModel.CheckConnectionAsync();

        // Assert
        Assert.False(viewModel.IsConnected);
        Assert.Equal("Disconnected", viewModel.ConnectionStatus);
        Assert.Equal("Red", viewModel.StatusColor);
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenThrows_ShouldPropagateException()
    {
        // Arrange
        var viewModel = new ClusterViewModel(_cluster, _mockClient);
        _mockClient.ValidateConnectionAsync(_cluster.Address)
            .Returns(Task.FromException<bool>(new Exception("Connection failed")));

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(() => viewModel.CheckConnectionAsync());
    }
}