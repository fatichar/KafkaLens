using System;
using System.Reflection;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using KafkaLens.Shared.Models;
using Xunit;
using GrpcFetchOptions = KafkaLens.Grpc.FetchOptions;
using GrpcMessage = KafkaLens.Grpc.Message;
using GrpcTopic = KafkaLens.Grpc.Topic;
using GrpcCluster = KafkaLens.Grpc.Cluster;

namespace KafkaLens.Clients;

public class GrpcClientTests
{
    private readonly GrpcClient client;

    public GrpcClientTests()
    {
        client = new GrpcClient("TestClient", "http://localhost:50051");
    }

    #region Constructor / Properties

    [Fact]
    public void Constructor_SetsName()
    {
        Assert.Equal("TestClient", client.Name);
    }

    [Fact]
    public void Constructor_SetsCanSaveMessages()
    {
        Assert.True(client.CanSaveMessages);
    }

    [Fact]
    public void CanEditClusters_ReturnsFalse()
    {
        Assert.False(client.CanEditClusters);
    }

    #endregion

    #region ToClusterModel (private static)

    [Fact]
    public void ToClusterModel_MapsAllProperties()
    {
        var grpcCluster = new GrpcCluster
        {
            Id = "cluster-1",
            Name = "MyCluster",
            BootstrapServers = "localhost:9092"
        };

        var result = InvokeStatic<KafkaCluster>("ToClusterModel", grpcCluster);

        Assert.Equal("cluster-1", result.Id);
        Assert.Equal("MyCluster", result.Name);
        Assert.Equal("localhost:9092", result.Address);
    }

    [Fact]
    public void ToClusterModel_WithIsConnected_SetsFlag()
    {
        var grpcCluster = new GrpcCluster
        {
            Id = "cluster-1",
            Name = "MyCluster",
            BootstrapServers = "localhost:9092",
            IsConnected = true
        };

        var result = InvokeStatic<KafkaCluster>("ToClusterModel", grpcCluster);

        Assert.True(result.IsConnected);
    }

    [Fact]
    public void ToClusterModel_WithoutIsConnected_DefaultsFalse()
    {
        var grpcCluster = new GrpcCluster
        {
            Id = "cluster-1",
            Name = "MyCluster",
            BootstrapServers = "localhost:9092"
        };

        var result = InvokeStatic<KafkaCluster>("ToClusterModel", grpcCluster);

        Assert.False(result.IsConnected);
    }

    #endregion

    #region ToTopicModel (private static)

    [Fact]
    public void ToTopicModel_MapsNameAndPartitionCount()
    {
        var grpcTopic = new GrpcTopic
        {
            Name = "my-topic",
            PartitionCount = 3
        };

        var result = InvokeStatic<Topic>("ToTopicModel", grpcTopic);

        Assert.Equal("my-topic", result.Name);
        Assert.Equal(3, result.PartitionCount);
    }

    [Fact]
    public void ToTopicModel_ZeroPartitions()
    {
        var grpcTopic = new GrpcTopic
        {
            Name = "empty-topic",
            PartitionCount = 0
        };

        var result = InvokeStatic<Topic>("ToTopicModel", grpcTopic);

        Assert.Equal("empty-topic", result.Name);
        Assert.Equal(0, result.PartitionCount);
    }

    #endregion

    #region ToMessageModel (private static)

    [Fact]
    public void ToMessageModel_MapsKeyAndValue()
    {
        var grpcMessage = new GrpcMessage
        {
            Key = ByteString.CopyFromUtf8("test-key"),
            Value = ByteString.CopyFromUtf8("test-value"),
            Offset = 42,
            Partition = 1,
            Timestamp = Timestamp.FromDateTimeOffset(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero))
        };

        var result = InvokeStatic<Message>("ToMessageModel", grpcMessage);

        Assert.Equal("test-key", result.KeyText);
        Assert.Equal(42, result.Offset);
        Assert.Equal(1, result.Partition);
    }

    [Fact]
    public void ToMessageModel_EmptyKeyAndValue()
    {
        var grpcMessage = new GrpcMessage
        {
            Key = ByteString.Empty,
            Value = ByteString.Empty,
            Offset = 0,
            Partition = 0,
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UnixEpoch)
        };

        var result = InvokeStatic<Message>("ToMessageModel", grpcMessage);

        Assert.NotNull(result);
        Assert.Equal(0, result.Offset);
    }

    #endregion

    #region ToGrpcFetchOptions (private static)

    [Fact]
    public void ToGrpcFetchOptions_MapsStartAndLimit()
    {
        var options = new FetchOptions(FetchPosition.START, 50);

        var result = InvokeStatic<GrpcFetchOptions>("ToGrpcFetchOptions", options);

        Assert.Equal(50u, result.MaxCount);
    }

    [Fact]
    public void ToGrpcFetchOptions_OffsetPosition_SetsOffset()
    {
        var position = new FetchPosition(PositionType.OFFSET, 100);
        var options = new FetchOptions(position, 10);

        var result = InvokeStatic<GrpcFetchOptions>("ToGrpcFetchOptions", options);

        Assert.Equal(100ul, result.Start.Offset);
    }

    [Fact]
    public void ToGrpcFetchOptions_TimestampPosition_SetsTimestamp()
    {
        var position = new FetchPosition(PositionType.TIMESTAMP, 1704067200000);
        var options = new FetchOptions(position, 10);

        var result = InvokeStatic<GrpcFetchOptions>("ToGrpcFetchOptions", options);

        Assert.NotNull(result.Start.Timestamp);
        Assert.Equal(1704067200, result.Start.Timestamp.Seconds);
    }

    #endregion

    #region ToGrpcTimestamp (private static)

    [Fact]
    public void ToGrpcTimestamp_ConvertsMillisecondsCorrectly()
    {
        long millis = 1704067200500; // 500ms past the second

        var result = InvokeStatic<Timestamp>("ToGrpcTimestamp", millis);

        Assert.Equal(1704067200, result.Seconds);
        Assert.Equal(500_000_000, result.Nanos);
    }

    [Fact]
    public void ToGrpcTimestamp_ZeroMilliseconds()
    {
        var result = InvokeStatic<Timestamp>("ToGrpcTimestamp", 0L);

        Assert.Equal(0, result.Seconds);
        Assert.Equal(0, result.Nanos);
    }

    [Fact]
    public void ToGrpcTimestamp_ExactSecond_ZeroNanos()
    {
        long millis = 1704067200000;

        var result = InvokeStatic<Timestamp>("ToGrpcTimestamp", millis);

        Assert.Equal(1704067200, result.Seconds);
        Assert.Equal(0, result.Nanos);
    }

    #endregion

    #region GetClusterByIdAsync / GetClusterByNameAsync

    [Fact]
    public async Task GetClusterByIdAsync_ThrowsNotImplementedException()
    {
        await Assert.ThrowsAsync<NotImplementedException>(() =>
            client.GetClusterByIdAsync("any"));
    }

    [Fact]
    public async Task GetClusterByNameAsync_ThrowsNotImplementedException()
    {
        await Assert.ThrowsAsync<NotImplementedException>(() =>
            client.GetClusterByNameAsync("any"));
    }

    #endregion

    #region GetMessagesAsync

    [Fact]
    public async Task GetMessagesAsync_Topic_ThrowsNotImplementedException()
    {
        var options = new FetchOptions(FetchPosition.START, 10);

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            client.GetMessagesAsync("cluster", "topic", options));
    }

    [Fact]
    public async Task GetMessagesAsync_Partition_ThrowsNotImplementedException()
    {
        var options = new FetchOptions(FetchPosition.START, 10);

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            client.GetMessagesAsync("cluster", "topic", 0, options));
    }

    #endregion

    #region UpdateClusterAsync

    [Fact]
    public async Task UpdateClusterAsync_ThrowsNotImplementedException()
    {
        var update = new KafkaClusterUpdate("name", "addr");

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            client.UpdateClusterAsync("id", update));
    }

    #endregion

    #region Helpers

    private static T InvokeStatic<T>(string methodName, params object[] args)
    {
        var method = typeof(GrpcClient).GetMethod(methodName,
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (T)method.Invoke(null, args)!;
    }

    #endregion
}
