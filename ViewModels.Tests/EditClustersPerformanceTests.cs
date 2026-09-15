using System.Collections.ObjectModel;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels;
using NSubstitute;
using Xunit;
using FluentAssertions;
using KafkaLens.Clients.Entities;

namespace KafkaLens.ViewModels.Tests;

public class EditClustersPerformanceTests
{
    [AvaloniaFact]
    public async Task StageSave_AddressAndDisable_PersistsBothWithoutChecking()
    {
        using var fixture = new DialogFixture();
        await fixture.Dialog.StageUpdateCluster(fixture.Live, "Renamed", "new:9092");
        fixture.Dialog.Clusters.Single().IsEnabled = false;
        Assert.Equal("Cluster", fixture.Live.Name);
        await fixture.Dialog.SaveAsync();
        fixture.ClusterRepo.Received(1).Update(Arg.Is<ClusterInfo>(c => c.Name == "Renamed" && c.Address == "new:9092" && !c.IsEnabled));
        await fixture.Local.DidNotReceiveWithAnyArgs().ValidateConnectionAsync(default!);
        Assert.False(fixture.Live.IsEnabled);
    }

    [AvaloniaFact]
    public async Task StageSave_PopulatedStatusOnlyChange_IsNoOp()
    {
        using var fixture = new DialogFixture();
        fixture.Live.Status = ConnectionState.Failed;
        fixture.Live.LastError = "Topic failure";
        await fixture.Dialog.SaveAsync();
        fixture.ClusterRepo.DidNotReceiveWithAnyArgs().Update(default!);
        fixture.ClientRepo.DidNotReceiveWithAnyArgs().Update(default!);
        await fixture.Factory.DidNotReceive().LoadClientsAsync();
        await fixture.Local.DidNotReceiveWithAnyArgs().GetAllClustersAsync();
        Assert.Empty(fixture.Refreshes);
    }

    [AvaloniaFact]
    public async Task StageSave_ClientTransportChange_RefreshesOnceWithoutExtraDiscovery()
    {
        using var fixture = new DialogFixture();
        var client = fixture.Dialog.Clients.Single();
        await fixture.Dialog.StageUpdateClient(new ClientInfo(client.Id, client.Name, "new-server", "grpc"));
        await fixture.Dialog.SaveAsync();
        Assert.Equal(new[] { "Remote" }, fixture.Refreshes);
        await fixture.Local.DidNotReceive().GetAllClustersAsync();
        await fixture.Local.DidNotReceiveWithAnyArgs().ValidateConnectionAsync(default!);
    }

    [AvaloniaFact]
    public async Task StageSave_ClientRename_DoesNotTriggerNetwork()
    {
        using var fixture = new DialogFixture();
        await fixture.Dialog.StageUpdateClient(new ClientInfo("client", "Renamed", "server", "grpc"));
        await fixture.Dialog.SaveAsync();
        Assert.Empty(fixture.Refreshes);
        await fixture.Local.DidNotReceive().GetAllClustersAsync();
    }

    [AvaloniaFact]
    public async Task TestStagedCluster_UpdatesCanonicalLiveStatus()
    {
        using var fixture = new DialogFixture();
        fixture.Local.ValidateConnectionAsync("broker:9092").Returns(false);
        await fixture.Dialog.TestConnectionAsync(fixture.Dialog.Clusters.Single(), "broker:9092");
        Assert.Equal(ConnectionState.Failed, fixture.Live.Status);
        Assert.Equal(fixture.Live.LastError, fixture.Dialog.Clusters.Single().LastError);
    }

    [AvaloniaFact]
    public async Task TestStagedChangedAddress_DoesNotUpdateCanonicalLiveStatus()
    {
        using var fixture = new DialogFixture();
        await fixture.Dialog.StageUpdateCluster(fixture.Live, "Staged", "new:9092");
        fixture.Local.ValidateConnectionAsync("new:9092").Returns(false);
        await fixture.Dialog.TestConnectionAsync(fixture.Dialog.Clusters.Single(), "new:9092");
        Assert.Equal(ConnectionState.Connected, fixture.Live.Status);
        Assert.Equal(ConnectionState.Failed, fixture.Dialog.Clusters.Single().Status);
    }

    [AvaloniaFact]
    public async Task UnknownDiagnostic_PreservesStagedFieldsAndKnownStatus()
    {
        using var fixture = new DialogFixture();
        await fixture.Dialog.StageUpdateCluster(fixture.Live, "Staged", "staged:9092");
        fixture.Factory.TestConnectionAsync(Arg.Any<ClientInfo>()).Returns(new[] { new KafkaCluster("cluster", "Discovered", "other:9092") });
        await fixture.Dialog.TestClientConnectionAsync(fixture.Dialog.Clients.Single(), "server");
        var staged = fixture.Dialog.Clusters.Single();
        Assert.Equal("Staged", staged.Name);
        Assert.Equal("staged:9092", staged.Address);
        Assert.Equal(ConnectionState.Connected, staged.Status);
        Assert.Equal(ConnectionState.Connected, fixture.Live.Status);
    }

    [AvaloniaFact]
    public async Task ClientDiagnosticException_UpdatesAffectedLiveRows()
    {
        using var fixture = new DialogFixture();
        fixture.Factory.TestConnectionAsync(Arg.Any<ClientInfo>()).Returns(Task.FromException<IEnumerable<KafkaCluster>>(new InvalidOperationException("Offline")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Dialog.TestClientConnectionAsync(fixture.Dialog.Clients.Single(), "server"));
        Assert.Equal(ConnectionState.Failed, fixture.Live.Status);
        Assert.Equal("Offline", fixture.Live.LastError);
    }

    [AvaloniaFact]
    public async Task DisabledStagedRow_RemainsDisabledAfterBackgroundLiveUpdate()
    {
        using var fixture = new DialogFixture();
        var staged = fixture.Dialog.Clusters.Single();
        staged.IsEnabled = false;
        await Task.Run(() => fixture.Live.Status = ConnectionState.Failed);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Disabled", staged.ConnectionStatus);
        Assert.Equal("Gray", staged.StatusColor);
        Assert.False(staged.IsChecking);
    }

    [AvaloniaFact]
    public async Task DisposedDialog_IgnoresQueuedLiveUpdates()
    {
        using var fixture = new DialogFixture();
        var staged = fixture.Dialog.Clusters.Single();
        await Task.Run(() =>
        {
            fixture.Live.Status = ConnectionState.Failed;
            fixture.Dialog.Dispose();
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(ConnectionState.Connected, staged.Status);
    }

    [AvaloniaFact]
    public void Reset_RemovesLiveSubscriptionsAndRows()
    {
        using var fixture = new DialogFixture();
        fixture.Clusters.Clear();
        fixture.Live.Status = ConnectionState.Failed;
        Assert.Empty(fixture.Dialog.Clusters);
        Assert.Equal(ConnectionState.Unknown, fixture.Dialog.Clients.Single().Status);
    }

    [AvaloniaFact]
    public async Task StageSave_DisabledOwnerBlocksChangedClusterCheck()
    {
        using var fixture = new DialogFixture();
        fixture.Dialog.Clients.Single().IsEnabled = false;
        await fixture.Dialog.StageUpdateCluster(fixture.Live, "Cluster", "new:9092");
        await fixture.Dialog.SaveAsync();
        await fixture.Local.DidNotReceiveWithAnyArgs().ValidateConnectionAsync(default!);
        Assert.Empty(fixture.Refreshes);
        Assert.Equal("Disabled", fixture.Live.ConnectionStatus);
    }

    [AvaloniaFact]
    public async Task StageSave_RejectedDisableIsNoOp()
    {
        using var fixture = new DialogFixture();
        fixture.Dialog.HasOpenTabsForCluster = _ => true;
        fixture.Dialog.ConfirmDisableCluster = _ => Task.FromResult(false);
        fixture.Dialog.Clusters.Single().IsEnabled = false;
        await fixture.Dialog.SaveAsync();
        fixture.ClusterRepo.DidNotReceiveWithAnyArgs().Update(default!);
        await fixture.Factory.DidNotReceive().LoadClientsAsync();
        Assert.True(fixture.Dialog.Clusters.Single().IsEnabled);
    }

    [AvaloniaFact]
    public async Task StageSave_SecondSaveDoesNotRepeatChanges()
    {
        using var fixture = new DialogFixture();
        await fixture.Dialog.StageUpdateCluster(fixture.Live, "Renamed", "broker:9092");
        await fixture.Dialog.SaveAsync();
        await fixture.Dialog.SaveAsync();
        fixture.ClusterRepo.Received(1).Update(Arg.Any<ClusterInfo>());
        await fixture.Local.DidNotReceiveWithAnyArgs().ValidateConnectionAsync(default!);
    }

    [AvaloniaFact]
    public void StageRemoveLiveArgument_DoesNotRemoveCanonicalUntilSave()
    {
        using var fixture = new DialogFixture();
        fixture.Dialog.StageRemoveCluster(fixture.Live);
        Assert.Empty(fixture.Dialog.Clusters);
        Assert.Single(fixture.Clusters);
        fixture.Live.Status = ConnectionState.Failed;
        Assert.Empty(fixture.Dialog.Clusters);
    }

    [AvaloniaFact]
    public async Task EmptyClientDiagnostic_DoesNotReportConnected()
    {
        using var fixture = new DialogFixture();
        fixture.Factory.TestConnectionAsync(Arg.Any<ClientInfo>()).Returns(Array.Empty<KafkaCluster>());
        var result = await fixture.Dialog.TestClientConnectionAsync("new-server");
        Assert.False(result.Succeeded);
    }

    [AvaloniaFact]
    public async Task TestChangedAddress_DisabledRowDoesNotCheck()
    {
        using var fixture = new DialogFixture();
        fixture.Dialog.Clusters.Single().IsEnabled = false;
        var result = await fixture.Dialog.TestConnectionAsync(fixture.Dialog.Clusters.Single(), "new:9092");
        Assert.False(result.Succeeded);
        await fixture.Local.DidNotReceiveWithAnyArgs().ValidateConnectionAsync(default!);
    }

    [AvaloniaFact]
    public void DisabledClientPresentation_IsRestoredOnEnable()
    {
        var row = new ClientInfoViewModel(new ClientInfo("client", "Client", "server", "grpc") { IsEnabled = false });
        row.Status = ConnectionState.Connected;
        Assert.Equal("Disabled", row.ConnectionStatus);
        Assert.Equal("Gray", row.StatusColor);
        row.IsEnabled = true;
        Assert.Equal("Connected", row.ConnectionStatus);
    }

    [AvaloniaFact]
    public async Task ConfiguredLocalCollision_DoesNotDisableOrReplaceBuiltInLocal()
    {
        using var fixture = new DialogFixture("Local");
        fixture.Dialog.Clients.Single().IsEnabled = false;
        await fixture.Dialog.SaveAsync();
        Assert.True(fixture.Live.IsAvailable);
        Assert.True(fixture.Dialog.Clusters.Single().IsAvailable);
        Assert.Same(fixture.Local, fixture.Live.Client);
        Assert.Empty(fixture.Refreshes);
        await fixture.Dialog.TestConnectionAsync(fixture.Dialog.Clusters.Single(), "broker:9092");
        await fixture.Local.Received(1).ValidateConnectionAsync("broker:9092");
    }

    [AvaloniaFact]
    public async Task ConfiguredLocalCollision_RemovalDoesNotRemoveBuiltInClusters()
    {
        using var fixture = new DialogFixture("Local");
        fixture.Dialog.StageRemoveClient(fixture.Dialog.Clients.Single());
        await fixture.Dialog.SaveAsync();
        Assert.Same(fixture.Live, Assert.Single(fixture.Clusters));
    }

    [AvaloniaFact]
    public async Task StagedClientAlias_DoesNotAggregateUnrelatedLiveClient()
    {
        using var fixture = new DialogFixture();
        var unrelatedClient = Substitute.For<IKafkaLensClient>();
        unrelatedClient.Name.Returns("Alias");
        fixture.Clusters.Add(new ClusterViewModel(new KafkaCluster("other", "Other", "other:9092")
            { Status = ConnectionState.Failed }, unrelatedClient));
        await fixture.Dialog.StageUpdateClient(new ClientInfo("client", "Alias", "server", "grpc"));
        fixture.Live.LastError = "changed";
        Assert.Equal(ConnectionState.Connected, fixture.Dialog.Clients.Single().Status);
    }

    [AvaloniaFact]
    public void StagedOwnerReenabled_MirrorsCanonicalKnownStatusAfterReset()
    {
        using var fixture = new DialogFixture();
        fixture.Dialog.Clients.Single().IsEnabled = false;
        fixture.Dialog.Clients.Single().IsEnabled = true;
        Assert.Equal(ConnectionState.Connected, fixture.Dialog.Clusters.Single().Status);
    }

    [AvaloniaFact]
    public async Task SaveAfterFactoryFailure_DoesNotAddClientTwice()
    {
        using var fixture = new DialogFixture();
        fixture.Factory.LoadClientsAsync().Returns(Task.FromException(new InvalidOperationException("Reload failed")), Task.CompletedTask);
        await fixture.Dialog.StageAddClientAsync("Added", "added-server");
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Dialog.SaveAsync());
        await fixture.Dialog.SaveAsync();
        fixture.ClientRepo.Received(1).Add(Arg.Is<ClientInfo>(c => c.Name == "Added"));
        Assert.Equal(new[] { "Added" }, fixture.Refreshes);
    }

    [AvaloniaFact]
    public async Task SaveAfterRefreshFailure_RetriesOnlyPendingRefresh()
    {
        using var fixture = new DialogFixture();
        var attempts = 0;
        using var dialog = new EditClustersViewModel(fixture.Clusters, fixture.ClusterRepo, fixture.ClientRepo, fixture.Factory, _ =>
            ++attempts == 1 ? Task.FromException(new InvalidOperationException("Refresh failed")) : Task.CompletedTask);
        await dialog.StageUpdateClient(new ClientInfo("client", "Remote", "new-server", "grpc"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => dialog.SaveAsync());
        await dialog.SaveAsync();
        fixture.ClientRepo.Received(1).Update(Arg.Any<ClientInfo>());
        await fixture.Factory.Received(1).LoadClientsAsync();
        Assert.Equal(2, attempts);
    }

    [AvaloniaFact]
    public async Task DelayedClusterTest_DoesNotOverwriteAnEditedAddress()
    {
        using var fixture = new DialogFixture();
        var completion = new TaskCompletionSource<bool>();
        fixture.Local.ValidateConnectionAsync("broker:9092").Returns(completion.Task);
        var test = fixture.Dialog.TestConnectionAsync(fixture.Dialog.Clusters.Single(), "broker:9092");
        fixture.Live.Address = "other:9092";
        await fixture.Dialog.StageUpdateCluster(fixture.Live, "Edited", "other:9092");
        completion.SetResult(false);
        await test;
        Assert.Equal(ConnectionState.Unknown, fixture.Live.Status);
        Assert.Equal(ConnectionState.Unknown, fixture.Dialog.Clusters.Single().Status);
    }

    private sealed class DialogFixture : IDisposable
    {
        public IClusterInfoRepository ClusterRepo { get; } = Substitute.For<IClusterInfoRepository>();
        public IClientInfoRepository ClientRepo { get; } = Substitute.For<IClientInfoRepository>();
        public IClientFactory Factory { get; } = Substitute.For<IClientFactory>();
        public IKafkaLensClient Local { get; } = Substitute.For<IKafkaLensClient>();
        public List<string> Refreshes { get; } = new();
        public ObservableCollection<ClusterViewModel> Clusters { get; }
        public ClusterViewModel Live { get; }
        public EditClustersViewModel Dialog { get; }

        public DialogFixture(string clientName = "Remote")
        {
            Local.Name.Returns(clientName);
            Local.CanEditClusters.Returns(true);
            var transports = new Dictionary<string, IKafkaLensClient> { ["Local"] = Local, [clientName] = Local };
            Factory.GetClient(Arg.Any<string>()).Returns(call =>
            {
                var name = call.Arg<string>();
                if (!transports.TryGetValue(name, out var transport))
                {
                    transport = Substitute.For<IKafkaLensClient>();
                    transport.Name.Returns(name);
                    transport.CanEditClusters.Returns(true);
                    transports[name] = transport;
                }
                return transport;
            });
            ClusterRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClusterInfo>(new Dictionary<string, ClusterInfo>
            {
                ["cluster"] = new("cluster", "Cluster", "broker:9092")
            }));
            var clients = new Dictionary<string, ClientInfo>
            {
                ["client"] = new("client", clientName, "server", "grpc")
            };
            ClientRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClientInfo>(clients));
            ClientRepo.When(r => r.Update(Arg.Any<ClientInfo>())).Do(call =>
            {
                var info = call.Arg<ClientInfo>();
                clients[info.Id] = info;
            });
            ClientRepo.When(r => r.Add(Arg.Any<ClientInfo>())).Do(call =>
            {
                var info = call.Arg<ClientInfo>();
                clients.Add(info.Id, info);
            });
            ClientRepo.When(r => r.Delete(Arg.Any<string>())).Do(call => clients.Remove(call.Arg<string>()));
            Live = new ClusterViewModel(new KafkaCluster("cluster", "Cluster", "broker:9092") { Status = ConnectionState.Connected }, Local);
            Clusters = new ObservableCollection<ClusterViewModel> { Live };
            Dialog = new EditClustersViewModel(Clusters, ClusterRepo, ClientRepo, Factory, name =>
            {
                Refreshes.Add(name);
                return Task.CompletedTask;
            });
        }

        public void Dispose() => Dialog.Dispose();
    }

    [AvaloniaFact]
    public void Constructor_ShouldNotCheckClientsAndShouldShowUnknownWithoutClusterStatus()
    {
        // Arrange
        var clusters = new ObservableCollection<ClusterViewModel>();
        var clusterRepo = Substitute.For<IClusterInfoRepository>();
        var clientRepo = Substitute.For<IClientInfoRepository>();
        var clientFactory = Substitute.For<IClientFactory>();

        var clientInfos = new Dictionary<string, ClientInfo>();
        for (int i = 0; i < 10; i++)
        {
            var id = i.ToString();
            var name = $"Client {i}";
            clientInfos.Add(id, new ClientInfo(id, name, "localhost", "grpc"));

            var mockClient = Substitute.For<IKafkaLensClient>();
            mockClient.Name.Returns(name);
            mockClient.GetAllClustersAsync().Returns(Task.FromResult<IEnumerable<KafkaCluster>>(new List<KafkaCluster>()));
            clientFactory.GetClient(name).Returns(mockClient);
        }
        clientRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClientInfo>(clientInfos));

        // Act
        var viewModel = new EditClustersViewModel(clusters, clusterRepo, clientRepo, clientFactory, _ => Task.CompletedTask);

        // Assert
        viewModel.Clients.Should().AllSatisfy(c => c.Status.Should().Be(ConnectionState.Unknown));
    }

    [AvaloniaFact]
    public void LiveClusterStatusChange_ShouldUpdateDialogClientStatus()
    {
        var clusters = new ObservableCollection<ClusterViewModel>();
        var clusterRepo = Substitute.For<IClusterInfoRepository>();
        var clientRepo = Substitute.For<IClientInfoRepository>();
        var clientFactory = Substitute.For<IClientFactory>();
        var remoteClient = Substitute.For<IKafkaLensClient>();
        remoteClient.Name.Returns("Remote");
        var liveCluster = new ClusterViewModel(new KafkaCluster("cluster-1", "Cluster", "broker:9092")
        {
            Status = ConnectionState.Failed
        }, remoteClient);
        clusters.Add(liveCluster);
        clientRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClientInfo>(new Dictionary<string, ClientInfo>
        {
            ["client-1"] = new("client-1", "Remote", "http://localhost:8080", "grpc")
        }));

        using var viewModel = new EditClustersViewModel(clusters, clusterRepo, clientRepo, clientFactory, _ => Task.CompletedTask);
        Assert.Equal(ConnectionState.Failed, viewModel.Clients.Single().Status);

        liveCluster.Status = ConnectionState.Connected;

        Assert.Equal(ConnectionState.Connected, viewModel.Clients.Single().Status);
    }

    [AvaloniaFact]
    public async Task SaveAsync_WithNoChanges_ShouldNotReloadOrCheckClients()
    {
        var clusters = new ObservableCollection<ClusterViewModel>();
        var clusterRepo = Substitute.For<IClusterInfoRepository>();
        var clientRepo = Substitute.For<IClientInfoRepository>();
        var clientFactory = Substitute.For<IClientFactory>();
        clusterRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClusterInfo>(new Dictionary<string, ClusterInfo>()));
        clientRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClientInfo>(new Dictionary<string, ClientInfo>()));

        using var viewModel = new EditClustersViewModel(clusters, clusterRepo, clientRepo, clientFactory, _ => Task.CompletedTask);

        await viewModel.SaveAsync();

        await clientFactory.DidNotReceive().LoadClientsAsync();
        clusterRepo.DidNotReceiveWithAnyArgs().Update(default!);
        clientRepo.DidNotReceiveWithAnyArgs().Update(default!);
    }

    [AvaloniaFact]
    public async Task UnknownClientClusterStatus_ShouldNotDowngradeLiveConnectedCluster()
    {
        var clusters = new ObservableCollection<ClusterViewModel>();
        var clusterRepo = Substitute.For<IClusterInfoRepository>();
        var clientRepo = Substitute.For<IClientInfoRepository>();
        var clientFactory = Substitute.For<IClientFactory>();
        var remoteClient = Substitute.For<IKafkaLensClient>();
        remoteClient.Name.Returns("Remote");
        var liveCluster = new ClusterViewModel(new KafkaCluster("cluster-1", "Cluster", "broker:9092")
        {
            Status = ConnectionState.Connected
        }, remoteClient);
        clusters.Add(liveCluster);
        clientRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClientInfo>(new Dictionary<string, ClientInfo>()));
        clientFactory.TestConnectionAsync(Arg.Any<ClientInfo>()).Returns(Task.FromResult<IEnumerable<KafkaCluster>>(
            new[] { new KafkaCluster("cluster-1", "Cluster", "broker:9092") }));
        using var viewModel = new EditClustersViewModel(clusters, clusterRepo, clientRepo, clientFactory, _ => Task.CompletedTask);

        await viewModel.TestClientConnectionAsync(new ClientInfoViewModel(
            new ClientInfo("client-1", "Remote", "http://localhost:8080", "grpc")), "http://localhost:8080");

        Assert.Equal(ConnectionState.Connected, liveCluster.Status);
    }

    [AvaloniaFact]
    public async Task TestClientConnectionAsync_ShouldPreserveRealFailedClusterDetails()
    {
        // Arrange
        var clusters = new ObservableCollection<ClusterViewModel>();
        var clusterRepo = Substitute.For<IClusterInfoRepository>();
        var clientRepo = Substitute.For<IClientInfoRepository>();
        var clientFactory = Substitute.For<IClientFactory>();
        var remoteClient = Substitute.For<IKafkaLensClient>();
        remoteClient.Name.Returns("Remote");
        remoteClient.CanEditClusters.Returns(false);
        var cluster = new ClusterViewModel(
            new KafkaCluster("cluster-1", "Cluster", "broker:9092")
            {
                Status = ConnectionState.Connected
            },
            remoteClient);
        clusters.Add(cluster);
        clientFactory.TestConnectionAsync(Arg.Any<ClientInfo>()).Returns(
            Task.FromResult<IEnumerable<KafkaCluster>>(new[]
            {
                new KafkaCluster("cluster-1", "Cluster", "broker:9092")
                {
                    Status = ConnectionState.Failed,
                    LastError = "Broker unavailable"
                }
            }));
        clientRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClientInfo>(new Dictionary<string, ClientInfo>()));
        var clientInfo = new ClientInfoViewModel(new ClientInfo("client-1", "Remote", "http://localhost:8080", "grpc"));
        using var viewModel = new EditClustersViewModel(clusters, clusterRepo, clientRepo, clientFactory, _ => Task.CompletedTask);

        // Act
        var result = await viewModel.TestClientConnectionAsync(clientInfo, "http://localhost:8080");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ConnectionState.Failed, cluster.Status);
        Assert.Equal("Broker unavailable", cluster.LastError);
    }

    [AvaloniaFact]
    public async Task TestConnectionAsync_ForCurrentAddress_ShouldUpdateClusterStatus()
    {
        // Arrange
        var clusters = new ObservableCollection<ClusterViewModel>();
        var clusterRepo = Substitute.For<IClusterInfoRepository>();
        var clientRepo = Substitute.For<IClientInfoRepository>();
        var clientFactory = Substitute.For<IClientFactory>();
        var localClient = Substitute.For<IKafkaLensClient, IConnectionTestClient>();
        localClient.Name.Returns("Local");
        localClient.CanEditClusters.Returns(true);
        ((IConnectionTestClient)localClient)
            .ValidateConnectionWithDetailsAsync("localhost:9092")
            .Returns(Task.FromResult(ConnectionValidationResult.Failed("VPN is disconnected")));
        clientFactory.GetClient("Local").Returns(localClient);
        clientRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClientInfo>(new Dictionary<string, ClientInfo>()));

        var cluster = new ClusterViewModel(
            new KafkaCluster("cluster-1", "Test", "localhost:9092")
            {
                Status = ConnectionState.Connected
            },
            localClient);
        clusters.Add(cluster);
        using var viewModel = new EditClustersViewModel(clusters, clusterRepo, clientRepo, clientFactory, _ => Task.CompletedTask);

        // Act
        var result = await viewModel.TestConnectionAsync(cluster, "localhost:9092");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ConnectionState.Failed, cluster.Status);
        Assert.Equal("VPN is disconnected", cluster.LastError);
    }

    [AvaloniaFact]
    public async Task TestConnectionAsync_ForUnsavedAddress_ShouldNotUpdateCurrentClusterStatus()
    {
        // Arrange
        var clusters = new ObservableCollection<ClusterViewModel>();
        var clusterRepo = Substitute.For<IClusterInfoRepository>();
        var clientRepo = Substitute.For<IClientInfoRepository>();
        var clientFactory = Substitute.For<IClientFactory>();
        var localClient = Substitute.For<IKafkaLensClient, IConnectionTestClient>();
        localClient.Name.Returns("Local");
        localClient.CanEditClusters.Returns(true);
        ((IConnectionTestClient)localClient)
            .ValidateConnectionWithDetailsAsync("new-host:9092")
            .Returns(Task.FromResult(ConnectionValidationResult.Failed("Invalid address")));
        clientFactory.GetClient("Local").Returns(localClient);
        clientRepo.GetAll().Returns(new ReadOnlyDictionary<string, ClientInfo>(new Dictionary<string, ClientInfo>()));

        var cluster = new ClusterViewModel(
            new KafkaCluster("cluster-1", "Test", "localhost:9092")
            {
                Status = ConnectionState.Connected
            },
            localClient);
        clusters.Add(cluster);
        using var viewModel = new EditClustersViewModel(clusters, clusterRepo, clientRepo, clientFactory, _ => Task.CompletedTask);

        // Act
        var result = await viewModel.TestConnectionAsync(cluster, "new-host:9092");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ConnectionState.Connected, cluster.Status);
    }
}
