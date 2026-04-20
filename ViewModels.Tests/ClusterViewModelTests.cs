using Avalonia.Headless.XUnit;
using KafkaLens.Shared;
using KafkaLens.ViewModels.Services;

namespace KafkaLens.ViewModels.Tests;

public class ClusterViewModelTests
{
    private readonly IFixture fixture;
    private readonly IKafkaLensClient mockClient;
    private readonly KafkaCluster cluster;

    public ClusterViewModelTests()
    {
        fixture = new Fixture();
        mockClient = Substitute.For<IKafkaLensClient>();
        cluster = fixture.Create<KafkaCluster>();
    }

    [Fact]
    public void Constructor_ShouldInitializeProperties()
    {
        // Act
        var viewModel = new ClusterViewModel(cluster, mockClient);

        // Assert
        Assert.Equal(mockClient, viewModel.Client);
        Assert.Equal(cluster.Id, viewModel.Id);
        Assert.Equal(cluster.Name, viewModel.Name);
        Assert.Equal(cluster.Address, viewModel.Address);
        Assert.Equal(cluster.Status, viewModel.Status);
        Assert.NotNull(viewModel.Topics);
        Assert.NotNull(viewModel.LoadTopicsCommand);
    }

    [Fact]
    public async Task CheckConnectionAsync_ShouldUpdateStatus()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(Task.FromResult(true));

        // Act
        await viewModel.CheckConnectionAsync();

        // Assert
        Assert.Equal(ConnectionState.Connected, viewModel.Status);
        await mockClient.Received(1).ValidateConnectionAsync(cluster.Address);
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenStatusIsKnown_ShouldKeepPreviousStatusWhileChecking()
    {
        // Arrange
        cluster.Status = ConnectionState.Connected;
        var viewModel = new ClusterViewModel(cluster, mockClient);
        var pendingResult = new TaskCompletionSource<bool>();
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(pendingResult.Task);

        // Act
        var checkTask = viewModel.CheckConnectionAsync();

        // Assert
        Assert.Equal(ConnectionState.Connected, viewModel.Status);
        Assert.Equal("Green", viewModel.StatusColor);
        Assert.False(viewModel.IsChecking);

        pendingResult.SetResult(false);
        await checkTask;

        Assert.Equal(ConnectionState.Failed, viewModel.Status);
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenStatusIsUnknown_ShouldShowCheckingUntilResultIsAvailable()
    {
        // Arrange
        cluster.Status = ConnectionState.Unknown;
        var viewModel = new ClusterViewModel(cluster, mockClient);
        var pendingResult = new TaskCompletionSource<bool>();
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(pendingResult.Task);

        // Act
        var checkTask = viewModel.CheckConnectionAsync();

        // Assert
        Assert.Equal(ConnectionState.Checking, viewModel.Status);
        Assert.True(viewModel.IsChecking);

        pendingResult.SetResult(true);
        await checkTask;

        Assert.Equal(ConnectionState.Connected, viewModel.Status);
    }

    [Fact]
    public async Task LoadTopicsAsync_ShouldLoadTopicsSuccessfully()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        var topics = fixture.CreateMany<Topic>().ToList();
        mockClient.GetTopicsAsync(cluster.Id).Returns((IList<Topic>)topics);

        // Act
        await viewModel.LoadTopicsCommand.ExecuteAsync(null);

        // Assert
        Assert.Equal(topics.Count, viewModel.Topics.Count);
        viewModel.Topics.Should().BeEquivalentTo(topics);
        Assert.Equal(ConnectionState.Connected, viewModel.Status);
        await mockClient.Received(1).GetTopicsAsync(cluster.Id);
    }

    [Fact]
    public async Task LoadTopicsAsync_ShouldHandleException()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        mockClient.GetTopicsAsync(cluster.Id).Returns(Task.FromException<IList<Topic>>(new Exception("Test error")));

        // Act
        await viewModel.LoadTopicsCommand.ExecuteAsync(null);

        // Assert
        Assert.Empty(viewModel.Topics);
        Assert.Equal(ConnectionState.Failed, viewModel.Status);
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenReturnsFalse_ShouldSetStatusToFailed()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(Task.FromResult(false));

        // Act
        await viewModel.CheckConnectionAsync();

        // Assert
        Assert.Equal(ConnectionState.Failed, viewModel.Status);
        Assert.Equal("Disconnected", viewModel.ConnectionStatus);
        Assert.Equal("Red", viewModel.StatusColor);
    }

    [Fact]
    public async Task CheckConnectionAsync_WhenValidationReturnsFalseButTopicsLoad_ShouldSetStatusToConnected()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        var topics = fixture.CreateMany<Topic>(2).ToList();
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(Task.FromResult(false));
        mockClient.GetTopicsAsync(cluster.Id).Returns((IList<Topic>)topics);

        // Act
        await viewModel.CheckConnectionAsync();

        // Assert
        Assert.Equal(ConnectionState.Connected, viewModel.Status);
        Assert.Null(viewModel.LastError);
        Assert.Equal(topics.Count, viewModel.Topics.Count);
        await mockClient.Received(1).ValidateConnectionAsync(cluster.Address);
        await mockClient.Received(1).GetTopicsAsync(cluster.Id);
    }

    [AvaloniaFact]
    public async Task CheckConnectionAsync_WhenThrows_ShouldLogFriendlyErrorWithoutStackTrace()
    {
        // Arrange
        var appLogService = new AppLogService();
        var viewModel = new ClusterViewModel(cluster, mockClient, appLogService);
        mockClient.ValidateConnectionAsync(cluster.Address)
            .Returns(Task.FromException<bool>(new Exception("Broker unavailable")));

        // Act
        await viewModel.CheckConnectionAsync();

        // Assert
        var entry = Assert.Single(appLogService.Entries.Where(e => e.Level == AppLogLevel.Error));
        Assert.Contains("Broker unavailable", entry.Message);
        Assert.DoesNotContain(" at ", entry.Message);
        Assert.DoesNotContain(nameof(Exception), entry.Message);
    }
}
