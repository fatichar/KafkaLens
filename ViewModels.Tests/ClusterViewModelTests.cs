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
        cluster = fixture.Build<KafkaCluster>().With(c => c.IsEnabled, true)
            .With(c => c.IsUnavailablePlaceholder, false).With(c => c.Status, ConnectionState.Unknown).Create();
    }

    [AvaloniaFact]
    public async Task MessageFailures_AreClearedOnlyBySuccessfulRetryOfTheSamePath()
    {
        var vm = new ClusterViewModel(cluster, mockClient);
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(true);
        vm.ReportMessageResult("orders", 0, new Exception("orders denied"), vm.ConnectionVersion);
        vm.ReportMessageResult("payments", 1, new Exception("payments denied"), vm.ConnectionVersion);
        await vm.CheckConnectionAsync(false);
        vm.ApplyDiagnosticStatus(ConnectionState.Unknown);
        vm.ApplyDiagnosticStatus(ConnectionState.Connected);
        Assert.Equal(ConnectionState.Failed, vm.Status);
        vm.ReportMessageResult("orders", 0, null, vm.ConnectionVersion);
        Assert.Equal(ConnectionState.Failed, vm.Status);
        Assert.Equal("payments denied", vm.LastError);
        vm.ReportMessageResult("payments", 1, null, vm.ConnectionVersion);
        Assert.Equal(ConnectionState.Connected, vm.Status);
    }

    [AvaloniaFact]
    public async Task AddressChange_DiscardsStaleValidationResult()
    {
        var vm = new ClusterViewModel(cluster, mockClient);
        var pending = new TaskCompletionSource<bool>();
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(pending.Task);
        var check = vm.CheckConnectionAsync(false);
        vm.Address = "changed:9092";
        pending.SetResult(true);
        await check;
        Assert.Equal(ConnectionState.Unknown, vm.Status);
        Assert.Empty(vm.Topics);
    }

    [AvaloniaFact]
    public async Task DisableDuringValidation_RemainsDisabledAfterCompletion()
    {
        var vm = new ClusterViewModel(cluster, mockClient);
        var pending = new TaskCompletionSource<bool>();
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(pending.Task);
        var check = vm.CheckConnectionAsync(false);
        vm.IsClientEnabled = false;
        pending.SetResult(true);
        await check;
        vm.Status = ConnectionState.Connected;
        Assert.False(vm.IsAvailable);
        Assert.Equal("Disabled", vm.ConnectionStatus);
        Assert.Equal("Gray", vm.StatusColor);
        Assert.False(vm.IsChecking);
    }

    [AvaloniaFact]
    public async Task CheckConnection_ForwardsCancellationToCapableClient()
    {
        var client = Substitute.For<IKafkaLensClient, ICancellableConnectionClient>();
        var capable = (ICancellableConnectionClient)client;
        using var cts = new System.Threading.CancellationTokenSource();
        capable.ValidateConnectionWithDetailsAsync(cluster.Address, Arg.Any<System.Threading.CancellationToken>())
            .Returns(async call =>
            {
                await Task.Delay(System.Threading.Timeout.Infinite, call.ArgAt<System.Threading.CancellationToken>(1));
                return ConnectionValidationResult.Success();
            });
        var vm = new ClusterViewModel(cluster, client);
        var check = vm.CheckConnectionAsync(false, cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => check);
        await client.DidNotReceive().ValidateConnectionAsync(Arg.Any<string>());
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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

    [AvaloniaFact]
    public async Task CheckConnectionAsync_WhenStatusIsKnown_ShouldKeepPreviousStatusWhileChecking()
    {
        // Arrange
        cluster.Status = ConnectionState.Connected;
        var viewModel = new ClusterViewModel(cluster, mockClient);
        var pendingResult = new TaskCompletionSource<bool>();
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(pendingResult.Task);
        mockClient.GetTopicsAsync(cluster.Id)
            .Returns(Task.FromException<IList<Topic>>(new Exception("Topic load failed")));

        // Act
        var checkTask = viewModel.CheckConnectionAsync();

        // Assert
        Assert.Equal(ConnectionState.Connected, viewModel.Status);
        Assert.Equal("Green", viewModel.StatusColor);
        Assert.True(viewModel.IsChecking);
        Assert.Equal("Checking...", viewModel.ConnectionStatus);

        pendingResult.SetResult(false);
        await checkTask;

        Assert.Equal(ConnectionState.Failed, viewModel.Status);
    }

    [AvaloniaFact]
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

    [AvaloniaFact]
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
        Assert.Equal(TopicLoadState.Loaded, viewModel.TopicLoadState);
        await mockClient.Received(1).GetTopicsAsync(cluster.Id);
    }

    [AvaloniaFact]
    public async Task LoadTopicsAsync_WhenAlreadyLoading_ShouldAwaitInFlightLoad()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        var topics = fixture.CreateMany<Topic>().ToList();
        var topicsTask = new TaskCompletionSource<IList<Topic>>(TaskCreationOptions.RunContinuationsAsynchronously);
        mockClient.GetTopicsAsync(cluster.Id).Returns(_ => topicsTask.Task);

        // Act
        var firstLoad = viewModel.EnsureTopicsLoadedAsync();
        var secondLoad = viewModel.EnsureTopicsLoadedAsync();

        Assert.False(secondLoad.IsCompleted);
        topicsTask.SetResult(topics);
        await Task.WhenAll(firstLoad, secondLoad);

        // Assert
        viewModel.Topics.Should().BeEquivalentTo(topics);
        Assert.Equal(TopicLoadState.Loaded, viewModel.TopicLoadState);
        await mockClient.Received(1).GetTopicsAsync(cluster.Id);
    }

    [AvaloniaFact]
    public async Task EnsureTopicsLoadedAsync_WhenAlreadyLoaded_ShouldReplayLoadedState()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        var topics = fixture.CreateMany<Topic>().ToList();
        mockClient.GetTopicsAsync(cluster.Id).Returns((IList<Topic>)topics);

        // Act
        await viewModel.EnsureTopicsLoadedAsync();
        await viewModel.EnsureTopicsLoadedAsync();

        // Assert
        viewModel.Topics.Should().BeEquivalentTo(topics);
        Assert.Equal(TopicLoadState.Loaded, viewModel.TopicLoadState);
        await mockClient.Received(1).GetTopicsAsync(cluster.Id);
    }

    [AvaloniaFact]
    public async Task EnsureTopicsLoadedAsync_WhenRefreshFails_ShouldKeepPreviousTopics()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        var topics = fixture.CreateMany<Topic>().ToList();
        mockClient.GetTopicsAsync(cluster.Id).Returns(
            Task.FromResult<IList<Topic>>(topics),
            Task.FromException<IList<Topic>>(new Exception("Refresh failed")),
            Task.FromException<IList<Topic>>(new Exception("Refresh failed")),
            Task.FromException<IList<Topic>>(new Exception("Refresh failed")));

        // Act
        await viewModel.EnsureTopicsLoadedAsync();
        await viewModel.EnsureTopicsLoadedAsync(forceRefresh: true);

        // Assert
        viewModel.Topics.Should().BeEquivalentTo(topics);
        Assert.Equal(TopicLoadState.Failed, viewModel.TopicLoadState);
    }

    [AvaloniaFact]
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
        Assert.Equal(TopicLoadState.Failed, viewModel.TopicLoadState);
    }

    [AvaloniaFact]
    public async Task CheckConnectionAsync_WhenReturnsFalse_ShouldSetStatusToFailed()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(Task.FromResult(false));
        mockClient.GetTopicsAsync(cluster.Id)
            .Returns(Task.FromException<IList<Topic>>(new Exception("Topic load failed")));

        // Act
        await viewModel.CheckConnectionAsync();

        // Assert
        Assert.Equal(ConnectionState.Failed, viewModel.Status);
        Assert.Equal("Disconnected", viewModel.ConnectionStatus);
        Assert.Equal("Red", viewModel.StatusColor);
    }

    [AvaloniaFact]
    public async Task CheckConnectionAsync_WhenValidationFails_ShouldNotReplaceFailureWithWeakerTopicMetadata()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        var topics = fixture.CreateMany<Topic>(2).ToList();
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(Task.FromResult(false));
        mockClient.GetTopicsAsync(cluster.Id).Returns((IList<Topic>)topics);

        // Act
        await viewModel.CheckConnectionAsync();

        // Assert
        Assert.Equal(ConnectionState.Failed, viewModel.Status);
        Assert.NotNull(viewModel.LastError);
        Assert.Empty(viewModel.Topics);
        await mockClient.Received(1).ValidateConnectionAsync(cluster.Address);
        await mockClient.DidNotReceive().GetTopicsAsync(cluster.Id);
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
        var entry = Assert.Single(appLogService.Entries, e => e.Level == AppLogLevel.Error);
        Assert.Contains("Broker unavailable", entry.Message);
        Assert.DoesNotContain(" at ", entry.Message);
        Assert.DoesNotContain(nameof(Exception), entry.Message);
    }

    [AvaloniaFact]
    public async Task CheckConnectionAsync_WhenDisabled_ShouldNotValidateConnection()
    {
        // Arrange
        var disabledCluster = fixture.Build<KafkaCluster>().With(c => c.IsEnabled, false).Create();
        var viewModel = new ClusterViewModel(disabledCluster, mockClient);

        // Act
        await viewModel.CheckConnectionAsync();

        // Assert
        await mockClient.DidNotReceive().ValidateConnectionAsync(Arg.Any<string>());
    }

    [AvaloniaFact]
    public async Task EnsureTopicsLoadedAsync_WhenDisabled_ShouldNotLoadTopics()
    {
        // Arrange
        var disabledCluster = fixture.Build<KafkaCluster>().With(c => c.IsEnabled, false).Create();
        var viewModel = new ClusterViewModel(disabledCluster, mockClient);

        // Act
        await viewModel.EnsureTopicsLoadedAsync();

        // Assert
        await mockClient.DidNotReceive().GetTopicsAsync(Arg.Any<string>());
    }

    [AvaloniaFact]
    public void SettingIsEnabled_ShouldPropagateToClusterModel()
    {
        // Arrange
        var viewModel = new ClusterViewModel(cluster, mockClient);
        Assert.True(viewModel.IsEnabled);
        Assert.True(cluster.IsEnabled);

        // Act
        viewModel.IsEnabled = false;

        // Assert
        Assert.False(cluster.IsEnabled);
    }
}
