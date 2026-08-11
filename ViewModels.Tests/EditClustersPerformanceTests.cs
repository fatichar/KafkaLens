using System.Collections.ObjectModel;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels;
using NSubstitute;
using Xunit;
using FluentAssertions;
using KafkaLens.Clients.Entities;

namespace KafkaLens.ViewModels.Tests;

public class EditClustersPerformanceTests
{
    [Fact]
    public async Task CheckClientConnectionsAsync_ShouldUpdateAllClientStatuses()
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
        var viewModel = new EditClustersViewModel(clusters, clusterRepo, clientRepo, clientFactory);

        // Since CheckClientConnectionsAsync is async void and called in constructor,
        // we need to wait for all clients to finish checking.
        // We'll poll the status with a timeout.
        var start = DateTime.Now;
        while (viewModel.Clients.Any(c => c.Status == ConnectionState.Checking) && (DateTime.Now - start).TotalSeconds < 5)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        // Assert
        viewModel.Clients.Should().AllSatisfy(c => c.Status.Should().Be(ConnectionState.Connected));
    }

    [Fact]
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
        using var viewModel = new EditClustersViewModel(clusters, clusterRepo, clientRepo, clientFactory);

        // Act
        var result = await viewModel.TestConnectionAsync(cluster, "localhost:9092");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ConnectionState.Failed, cluster.Status);
        Assert.Equal("VPN is disconnected", cluster.LastError);
    }

    [Fact]
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
        using var viewModel = new EditClustersViewModel(clusters, clusterRepo, clientRepo, clientFactory);

        // Act
        var result = await viewModel.TestConnectionAsync(cluster, "new-host:9092");

        // Assert
        Assert.False(result.Succeeded);
        Assert.Equal(ConnectionState.Connected, cluster.Status);
    }
}
