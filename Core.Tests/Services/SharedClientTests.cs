using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using KafkaLens.Core.Services;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using NSubstitute;
using Xunit;

namespace KafkaLens.Core.Tests.Services;

public class SharedClientTests
{
    private readonly IClusterInfoRepository _infoRepository;
    private readonly ConsumerFactory _consumerFactory;
    private readonly SharedClient _sut;

    public SharedClientTests()
    {
        _infoRepository = Substitute.For<IClusterInfoRepository>();
        _consumerFactory = Substitute.For<ConsumerFactory>();
        _sut = new SharedClient(_infoRepository, _consumerFactory);
    }

    #region Properties

    [Fact]
    public void Name_ReturnsShared()
    {
        Assert.Equal("Shared", _sut.Name);
    }

    [Fact]
    public void CanEditClusters_ReturnsFalse()
    {
        Assert.False(_sut.CanEditClusters);
    }

    [Fact]
    public void CanSaveMessages_ReturnsTrue()
    {
        Assert.True(_sut.CanSaveMessages);
    }

    #endregion Properties

    #region GetAllClustersAsync

    [Fact]
    public async Task GetAllClustersAsync_NoClusters_ReturnsEmpty()
    {
        // Arrange
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(
            new Dictionary<string, Shared.Entities.ClusterInfo>()));

        // Act
        var result = await _sut.GetAllClustersAsync();

        // Assert
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllClustersAsync_WithClusters_ReturnsAll()
    {
        // Arrange
        var cluster1 = new Shared.Entities.ClusterInfo("id1", "cluster1", "localhost:9092");
        var cluster2 = new Shared.Entities.ClusterInfo("id2", "cluster2", "localhost:9093");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo>
        {
            { "id1", cluster1 },
            { "id2", cluster2 }
        };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        // Act
        var result = (await _sut.GetAllClustersAsync()).ToList();

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.Id == "id1" && c.Name == "cluster1" && c.Address == "localhost:9092");
        Assert.Contains(result, c => c.Id == "id2" && c.Name == "cluster2" && c.Address == "localhost:9093");
    }

    #endregion GetAllClustersAsync

    #region GetClusterByIdAsync

    [Fact]
    public async Task GetClusterByIdAsync_ValidId_ReturnsCluster()
    {
        // Arrange
        var cluster = new Shared.Entities.ClusterInfo("id1", "cluster1", "localhost:9092");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo> { { "id1", cluster } };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        // Act
        var result = await _sut.GetClusterByIdAsync("id1");

        // Assert
        Assert.Equal("id1", result.Id);
        Assert.Equal("cluster1", result.Name);
        Assert.Equal("localhost:9092", result.Address);
    }

    [Fact]
    public async Task GetClusterByIdAsync_InvalidId_ThrowsArgumentException()
    {
        // Arrange
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(
            new Dictionary<string, Shared.Entities.ClusterInfo>()));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.GetClusterByIdAsync("nonexistent"));
    }

    #endregion GetClusterByIdAsync

    #region AddAsync

    [Fact]
    public async Task AddAsync_NewCluster_AddsToRepository()
    {
        // Arrange
        var newCluster = new NewKafkaCluster("test-cluster", "localhost:9092");
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(
            new Dictionary<string, Shared.Entities.ClusterInfo>()));

        // Act
        var result = await _sut.AddAsync(newCluster);

        // Assert
        Assert.Equal("test-cluster", result.Name);
        Assert.Equal("localhost:9092", result.Address);
        Assert.NotNull(result.Id);
        _infoRepository.Received(1).Add(Arg.Is<Shared.Entities.ClusterInfo>(c =>
            c.Name == "test-cluster" && c.Address == "localhost:9092"));
    }

    [Fact]
    public async Task AddAsync_DuplicateName_ThrowsArgumentException()
    {
        // Arrange
        var existing = new Shared.Entities.ClusterInfo("id1", "test-cluster", "localhost:9092");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo> { { "id1", existing } };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        var newCluster = new NewKafkaCluster("test-cluster", "localhost:9093");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.AddAsync(newCluster));
    }

    [Fact]
    public async Task AddAsync_DuplicateNameCaseInsensitive_ThrowsArgumentException()
    {
        // Arrange
        var existing = new Shared.Entities.ClusterInfo("id1", "Test-Cluster", "localhost:9092");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo> { { "id1", existing } };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        var newCluster = new NewKafkaCluster("test-cluster", "localhost:9093");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.AddAsync(newCluster));
    }

    #endregion AddAsync

    #region UpdateClusterAsync

    [Fact]
    public async Task UpdateClusterAsync_ValidId_UpdatesCluster()
    {
        // Arrange
        var cluster = new Shared.Entities.ClusterInfo("id1", "old-name", "localhost:9092");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo> { { "id1", cluster } };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        var update = new KafkaClusterUpdate("new-name", "localhost:9093");

        // Act
        var result = await _sut.UpdateClusterAsync("id1", update);

        // Assert
        Assert.Equal("new-name", result.Name);
        Assert.Equal("localhost:9093", result.Address);
        _infoRepository.Received(1).Update(Arg.Is<Shared.Entities.ClusterInfo>(c =>
            c.Name == "new-name" && c.Address == "localhost:9093"));
    }

    [Fact]
    public async Task UpdateClusterAsync_InvalidId_ThrowsArgumentException()
    {
        // Arrange
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(
            new Dictionary<string, Shared.Entities.ClusterInfo>()));

        var update = new KafkaClusterUpdate("name", "localhost:9092");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.UpdateClusterAsync("nonexistent", update));
    }

    #endregion UpdateClusterAsync

    #region RemoveClusterByIdAsync

    [Fact]
    public async Task RemoveClusterByIdAsync_CallsRepositoryDelete()
    {
        // Arrange
        var clusterId = "id1";

        // Act
        await _sut.RemoveClusterByIdAsync(clusterId);

        // Assert
        _infoRepository.Received(1).Delete(clusterId);
    }

    #endregion RemoveClusterByIdAsync

    #region ValidateConnectionAsync

    [Fact]
    public async Task ValidateConnectionAsync_ReturnsFalse()
    {
        // Act
        var result = await _sut.ValidateConnectionAsync("localhost:9092");

        // Assert
        Assert.False(result);
    }

    #endregion ValidateConnectionAsync

    #region GetTopicsAsync

    [Fact]
    public async Task GetTopicsAsync_ValidCluster_ReturnsSortedTopics()
    {
        // Arrange
        var cluster = new Shared.Entities.ClusterInfo("id1", "cluster1", "localhost:9092");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo> { { "id1", cluster } };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        var mockConsumer = Substitute.For<IKafkaConsumer>();
        _consumerFactory.CreateNew("localhost:9092").Returns(mockConsumer);

        var topics = new List<Topic>
        {
            new Topic("_internal", 1),
            new Topic("beta", 2),
            new Topic("alpha", 3)
        };
        mockConsumer.GetTopics().Returns(topics);

        // Act
        var result = await _sut.GetTopicsAsync("id1");

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal("alpha", result[0].Name);
        Assert.Equal("beta", result[1].Name);
        Assert.Equal("_internal", result[2].Name);
    }

    [Fact]
    public async Task GetTopicsAsync_InvalidCluster_ThrowsArgumentException()
    {
        // Arrange
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(
            new Dictionary<string, Shared.Entities.ClusterInfo>()));

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.GetTopicsAsync("nonexistent"));
    }

    #endregion GetTopicsAsync

    #region GetMessagesAsync

    [Fact]
    public async Task GetMessagesAsync_TopicOnly_DelegatesToConsumer()
    {
        // Arrange
        var cluster = new Shared.Entities.ClusterInfo("id1", "cluster1", "localhost:9092");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo> { { "id1", cluster } };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        var mockConsumer = Substitute.For<IKafkaConsumer>();
        _consumerFactory.CreateNew("localhost:9092").Returns(mockConsumer);

        var expectedMessages = new List<Message>();
        mockConsumer.GetMessagesAsync("test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(expectedMessages);

        var options = new FetchOptions(FetchPosition.START, 10);

        // Act
        var result = await _sut.GetMessagesAsync("id1", "test-topic", options);

        // Assert
        Assert.Same(expectedMessages, result);
    }

    [Fact]
    public async Task GetMessagesAsync_WithPartition_DelegatesToConsumer()
    {
        // Arrange
        var cluster = new Shared.Entities.ClusterInfo("id1", "cluster1", "localhost:9092");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo> { { "id1", cluster } };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        var mockConsumer = Substitute.For<IKafkaConsumer>();
        _consumerFactory.CreateNew("localhost:9092").Returns(mockConsumer);

        var expectedMessages = new List<Message>();
        mockConsumer.GetMessagesAsync("test-topic", 0, Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(expectedMessages);

        var options = new FetchOptions(FetchPosition.START, 10);

        // Act
        var result = await _sut.GetMessagesAsync("id1", "test-topic", 0, options);

        // Assert
        Assert.Same(expectedMessages, result);
    }

    #endregion GetMessagesAsync

    #region GetMessageStream

    [Fact]
    public void GetMessageStream_TopicOnly_DelegatesToConsumer()
    {
        // Arrange
        var cluster = new Shared.Entities.ClusterInfo("id1", "cluster1", "localhost:9092");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo> { { "id1", cluster } };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        var mockConsumer = Substitute.For<IKafkaConsumer>();
        _consumerFactory.CreateNew("localhost:9092").Returns(mockConsumer);

        var expectedStream = new MessageStream();
        mockConsumer.GetMessageStream("test-topic", Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(expectedStream);

        var options = new FetchOptions(FetchPosition.START, 10);

        // Act
        var result = _sut.GetMessageStream("id1", "test-topic", options);

        // Assert
        Assert.Same(expectedStream, result);
    }

    [Fact]
    public void GetMessageStream_WithPartition_DelegatesToConsumer()
    {
        // Arrange
        var cluster = new Shared.Entities.ClusterInfo("id1", "cluster1", "localhost:9092");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo> { { "id1", cluster } };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        var mockConsumer = Substitute.For<IKafkaConsumer>();
        _consumerFactory.CreateNew("localhost:9092").Returns(mockConsumer);

        var expectedStream = new MessageStream();
        mockConsumer.GetMessageStream("test-topic", 0, Arg.Any<FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(expectedStream);

        var options = new FetchOptions(FetchPosition.START, 10);

        // Act
        var result = _sut.GetMessageStream("id1", "test-topic", 0, options);

        // Assert
        Assert.Same(expectedStream, result);
    }

    #endregion GetMessageStream

    #region Consumer Caching

    [Fact]
    public async Task GetTopicsAsync_CalledTwice_ReusesConsumer()
    {
        // Arrange
        var cluster = new Shared.Entities.ClusterInfo("id1", "cluster1", "localhost:9092");
        var dict = new Dictionary<string, Shared.Entities.ClusterInfo> { { "id1", cluster } };
        _infoRepository.GetAll().Returns(new ReadOnlyDictionary<string, Shared.Entities.ClusterInfo>(dict));

        var mockConsumer = Substitute.For<IKafkaConsumer>();
        _consumerFactory.CreateNew("localhost:9092").Returns(mockConsumer);
        mockConsumer.GetTopics().Returns(new List<Topic>());

        // Act
        await _sut.GetTopicsAsync("id1");
        await _sut.GetTopicsAsync("id1");

        // Assert
        _consumerFactory.Received(1).CreateNew("localhost:9092");
    }

    #endregion Consumer Caching
}
