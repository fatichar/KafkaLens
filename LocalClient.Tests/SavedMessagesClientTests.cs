using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using KafkaLens.Shared.Models;
using Xunit;

namespace KafkaLens.Clients;

public class SavedMessagesClientTests
{
    private readonly SavedMessagesClient client;

    public SavedMessagesClientTests()
    {
        client = new SavedMessagesClient();
    }

    #region Properties

    [Fact]
    public void Name_ReturnsSavedMessages()
    {
        Assert.Equal("Saved Messages", client.Name);
    }

    [Fact]
    public void CanEditClusters_ReturnsFalse()
    {
        Assert.False(client.CanEditClusters);
    }

    [Fact]
    public void CanSaveMessages_ReturnsFalse()
    {
        Assert.False(client.CanSaveMessages);
    }

    #endregion

    #region ValidateConnectionAsync

    [Fact]
    public async Task ValidateConnectionAsync_AlwaysReturnsFalse()
    {
        var result = await client.ValidateConnectionAsync("localhost:9092");

        Assert.False(result);
    }

    #endregion

    #region AddAsync

    [Fact]
    public async Task AddAsync_ValidCluster_ReturnsKafkaCluster()
    {
        var newCluster = new NewKafkaCluster("TestCluster", "C:\\test\\messages");

        var result = await client.AddAsync(newCluster);

        Assert.NotNull(result);
        Assert.Equal("TestCluster", result.Name);
        Assert.Equal("C:\\test\\messages", result.Address);
        Assert.NotNull(result.Id);
    }

    [Fact]
    public async Task AddAsync_DuplicateName_ThrowsArgumentException()
    {
        var newCluster1 = new NewKafkaCluster("TestCluster", "C:\\test1");
        await client.AddAsync(newCluster1);

        var newCluster2 = new NewKafkaCluster("TestCluster", "C:\\test2");

        await Assert.ThrowsAsync<ArgumentException>(() => client.AddAsync(newCluster2));
    }

    [Fact]
    public async Task AddAsync_DuplicateNameCaseInsensitive_ThrowsArgumentException()
    {
        var newCluster1 = new NewKafkaCluster("TestCluster", "C:\\test1");
        await client.AddAsync(newCluster1);

        var newCluster2 = new NewKafkaCluster("testcluster", "C:\\test2");

        await Assert.ThrowsAsync<ArgumentException>(() => client.AddAsync(newCluster2));
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
    public async Task GetAllClustersAsync_AfterAdd_ReturnsAddedCluster()
    {
        await client.AddAsync(new NewKafkaCluster("Cluster1", "C:\\test1"));

        var result = (await client.GetAllClustersAsync()).ToList();

        Assert.Single(result);
        Assert.Equal("Cluster1", result[0].Name);
    }

    [Fact]
    public async Task GetAllClustersAsync_MultipleClusters_ReturnsAll()
    {
        await client.AddAsync(new NewKafkaCluster("Cluster1", "C:\\test1"));
        await client.AddAsync(new NewKafkaCluster("Cluster2", "C:\\test2"));

        var result = (await client.GetAllClustersAsync()).ToList();

        Assert.Equal(2, result.Count);
    }

    #endregion

    #region GetClusterByIdAsync

    [Fact]
    public async Task GetClusterByIdAsync_ExistingId_ReturnsCluster()
    {
        var added = await client.AddAsync(new NewKafkaCluster("TestCluster", "C:\\test"));

        var result = await client.GetClusterByIdAsync(added.Id);

        Assert.Equal(added.Id, result.Id);
        Assert.Equal("TestCluster", result.Name);
    }

    [Fact]
    public async Task GetClusterByIdAsync_NonExistingId_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            client.GetClusterByIdAsync("nonexistent"));
    }

    #endregion

    #region RemoveClusterByIdAsync

    [Fact]
    public async Task RemoveClusterByIdAsync_ExistingCluster_RemovesIt()
    {
        var added = await client.AddAsync(new NewKafkaCluster("TestCluster", "C:\\test"));

        await client.RemoveClusterByIdAsync(added.Id);

        var result = (await client.GetAllClustersAsync()).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public async Task RemoveClusterByIdAsync_NonExistingId_ThrowsKeyNotFoundException()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            client.RemoveClusterByIdAsync("nonexistent"));
    }

    #endregion
}
