using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using NSubstitute;
using Xunit;

namespace KafkaLens.Clients;

public class LocalClientTests
{
    private readonly IClusterInfoRepository repository;
    private readonly LocalClient client;

    public LocalClientTests()
    {
        repository = Substitute.For<IClusterInfoRepository>();
        repository.GetAll().Returns(new ReadOnlyDictionary<string, ClusterInfo>(
            new Dictionary<string, ClusterInfo>()));
        client = new LocalClient(repository);
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
