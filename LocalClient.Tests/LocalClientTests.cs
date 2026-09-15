using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KafkaLens.Core.Services;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using NSubstitute;
using Xunit;

namespace KafkaLens.Clients.Tests;

public class LocalClientTests
{
    private readonly IClusterInfoRepository repository;
    private readonly LocalClient client;

    public LocalClientTests()
    {
        repository = Substitute.For<IClusterInfoRepository>();
        repository.GetAll().Returns(new ReadOnlyDictionary<string, ClusterInfo>(
            new Dictionary<string, ClusterInfo>()));
        client = new LocalClient(repository, new KafkaConfig());
    }

    [Fact]
    public async Task DisabledCluster_DoesNotCreateOrValidateConsumer()
    {
        var cluster = new ClusterInfo("id1", "disabled", "broker:9092") { IsEnabled = false };
        repository.GetAll().Returns(new ReadOnlyDictionary<string, ClusterInfo>(
            new Dictionary<string, ClusterInfo> { [cluster.Id] = cluster }));
        var factory = Substitute.For<ConsumerFactory>(new KafkaConfig());
        var sut = new LocalClient(repository, new KafkaConfig(), factory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.GetTopicsAsync(cluster.Id));
        Assert.False((await sut.ValidateConnectionWithDetailsAsync(cluster.Address, CancellationToken.None)).Succeeded);
        factory.DidNotReceiveWithAnyArgs().CreateNew(default!);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TemporaryValidation_DisposesConsumerOnSuccessAndFailure(bool fails)
    {
        var native = Substitute.For<IKafkaConsumer>();
        native.ValidateConnectionWithDetailsAsync(Arg.Any<CancellationToken>()).Returns(_ => fails
            ? Task.FromException<ConnectionValidationResult>(new InvalidOperationException("Probe failed"))
            : Task.FromResult(ConnectionValidationResult.Success()));
        var factory = Substitute.For<ConsumerFactory>(new KafkaConfig());
        factory.CreateNew("broker:9092").Returns(native);
        var sut = new LocalClient(repository, new KafkaConfig(), factory);

        var result = await sut.ValidateConnectionWithDetailsAsync("broker:9092", CancellationToken.None);

        Assert.Equal(!fails, result.Succeeded);
        native.Received(1).Dispose();
        native.DidNotReceive().ValidateConnectionWithDetails();
    }

    [Fact]
    public async Task TemporaryValidation_PropagatesCancellationAndDisposesConsumer()
    {
        using var cancellation = new CancellationTokenSource();
        var native = Substitute.For<IKafkaConsumer>();
        native.ValidateConnectionWithDetailsAsync(cancellation.Token).Returns(_ =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<ConnectionValidationResult>(cancellation.Token);
        });
        var factory = Substitute.For<ConsumerFactory>(new KafkaConfig());
        factory.CreateNew("broker:9092").Returns(native);
        var sut = new LocalClient(repository, new KafkaConfig(), factory);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sut.ValidateConnectionWithDetailsAsync("broker:9092", cancellation.Token));

        native.Received(1).Dispose();
    }

    [Fact]
    public async Task ConcurrentAccess_CreatesOneConsumer_AndAddressChangeEvictsIt()
    {
        var cluster = new ClusterInfo("id1", "enabled", "broker:9092");
        repository.GetAll().Returns(new ReadOnlyDictionary<string, ClusterInfo>(
            new Dictionary<string, ClusterInfo> { [cluster.Id] = cluster }));
        var native = Substitute.For<IKafkaConsumer>();
        var replacement = Substitute.For<IKafkaConsumer>();
        native.GetTopics().Returns(new List<Topic>());
        replacement.GetTopics().Returns(new List<Topic>());
        var factory = Substitute.For<ConsumerFactory>(new KafkaConfig());
        factory.CreateNew("broker:9092").Returns(native);
        factory.CreateNew("other:9092").Returns(replacement);
        var sut = new LocalClient(repository, new KafkaConfig(), factory);

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => sut.GetTopicsAsync(cluster.Id))));
        factory.Received(1).CreateNew("broker:9092");
        await sut.UpdateClusterAsync(cluster.Id, new KafkaClusterUpdate(cluster.Name, "other:9092"));
        await sut.GetTopicsAsync(cluster.Id);

        native.Received(1).Dispose();
        replacement.Received(1).GetTopics();
    }

    #region Properties

    [Fact]
    public void Name_ReturnsLocal()
    {
        Assert.Equal("Local", client.Name);
    }

    [Fact]
    public void CanEditClusters_ReturnsTrue()
    {
        Assert.True(client.CanEditClusters);
    }

    [Fact]
    public void CanSaveMessages_ReturnsTrue()
    {
        Assert.True(client.CanSaveMessages);
    }

    #endregion

    #region AddAsync

    [Fact]
    public async Task AddAsync_ValidCluster_ReturnsKafkaCluster()
    {
        var newCluster = new NewKafkaCluster("TestCluster", "localhost:9092");

        var result = await client.AddAsync(newCluster);

        Assert.NotNull(result);
        Assert.Equal("TestCluster", result.Name);
        Assert.Equal("localhost:9092", result.Address);
        Assert.NotNull(result.Id);
    }

    [Fact]
    public async Task AddAsync_ValidCluster_CallsRepositoryAdd()
    {
        var newCluster = new NewKafkaCluster("TestCluster", "localhost:9092");

        await client.AddAsync(newCluster);

        repository.Received(1).Add(Arg.Is<ClusterInfo>(c =>
            c.Name == "TestCluster" && c.Address == "localhost:9092"));
    }

    [Fact]
    public async Task AddAsync_DuplicateName_ThrowsArgumentException()
    {
        var existing = new ClusterInfo("id1", "TestCluster", "localhost:9092");
        var clusters = new ReadOnlyDictionary<string, ClusterInfo>(
            new Dictionary<string, ClusterInfo> { { "id1", existing } });
        repository.GetAll().Returns(clusters);

        var newCluster = new NewKafkaCluster("TestCluster", "localhost:9093");

        await Assert.ThrowsAsync<ArgumentException>(() => client.AddAsync(newCluster));
    }

    [Fact]
    public async Task AddAsync_DuplicateNameCaseInsensitive_ThrowsArgumentException()
    {
        var existing = new ClusterInfo("id1", "TestCluster", "localhost:9092");
        var clusters = new ReadOnlyDictionary<string, ClusterInfo>(
            new Dictionary<string, ClusterInfo> { { "id1", existing } });
        repository.GetAll().Returns(clusters);

        var newCluster = new NewKafkaCluster("testcluster", "localhost:9093");

        await Assert.ThrowsAsync<ArgumentException>(() => client.AddAsync(newCluster));
    }

    #endregion

    #region GetAllClustersAsync

    [Fact]
    public async Task GetAllClustersAsync_NoClusters_ReturnsEmpty()
    {
        var result = await client.GetAllClustersAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllClustersAsync_WithClusters_ReturnsAll()
    {
        var cluster1 = new ClusterInfo("id1", "Cluster1", "localhost:9092");
        var cluster2 = new ClusterInfo("id2", "Cluster2", "localhost:9093");
        var clusters = new ReadOnlyDictionary<string, ClusterInfo>(
            new Dictionary<string, ClusterInfo>
            {
                { "id1", cluster1 },
                { "id2", cluster2 }
            });
        repository.GetAll().Returns(clusters);

        var result = (await client.GetAllClustersAsync()).ToList();

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task GetAllClustersAsync_MapsPropertiesCorrectly()
    {
        var cluster = new ClusterInfo("id1", "MyCluster", "localhost:9092");
        var clusters = new ReadOnlyDictionary<string, ClusterInfo>(
            new Dictionary<string, ClusterInfo> { { "id1", cluster } });
        repository.GetAll().Returns(clusters);

        var result = (await client.GetAllClustersAsync()).ToList();

        Assert.Single(result);
        Assert.Equal("id1", result[0].Id);
        Assert.Equal("MyCluster", result[0].Name);
        Assert.Equal("localhost:9092", result[0].Address);
    }

    #endregion

    #region GetClusterByIdAsync

    [Fact]
    public async Task GetClusterByIdAsync_ExistingId_ReturnsCluster()
    {
        var cluster = new ClusterInfo("id1", "MyCluster", "localhost:9092");
        var clusters = new ReadOnlyDictionary<string, ClusterInfo>(
            new Dictionary<string, ClusterInfo> { { "id1", cluster } });
        repository.GetAll().Returns(clusters);

        var result = await client.GetClusterByIdAsync("id1");

        Assert.Equal("id1", result.Id);
        Assert.Equal("MyCluster", result.Name);
    }

    [Fact]
    public async Task GetClusterByIdAsync_NonExistingId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetClusterByIdAsync("nonexistent"));
    }

    #endregion

    #region UpdateClusterAsync

    [Fact]
    public async Task UpdateClusterAsync_ExistingCluster_UpdatesAndReturns()
    {
        var cluster = new ClusterInfo("id1", "OldName", "localhost:9092");
        var clusters = new ReadOnlyDictionary<string, ClusterInfo>(
            new Dictionary<string, ClusterInfo> { { "id1", cluster } });
        repository.GetAll().Returns(clusters);

        var update = new KafkaClusterUpdate("NewName", "localhost:9093");
        var result = await client.UpdateClusterAsync("id1", update);

        Assert.Equal("NewName", result.Name);
        Assert.Equal("localhost:9093", result.Address);
        repository.Received(1).Update(Arg.Is<ClusterInfo>(c => c.Id == "id1"));
    }

    [Fact]
    public async Task UpdateClusterAsync_NonExistingCluster_ThrowsArgumentException()
    {
        var update = new KafkaClusterUpdate("NewName", "localhost:9093");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.UpdateClusterAsync("nonexistent", update));
    }

    #endregion

    #region RemoveClusterByIdAsync

    [Fact]
    public async Task RemoveClusterByIdAsync_CallsRepositoryDelete()
    {
        await client.RemoveClusterByIdAsync("id1");

        repository.Received(1).Delete("id1");
    }

    #endregion
}