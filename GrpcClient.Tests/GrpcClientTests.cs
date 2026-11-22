using System;
using System.Reflection;
using System.Threading.Tasks;
using Google.Protobuf;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using KafkaLens.Shared.Models;
using NSubstitute;
using Xunit;
using GrpcFetchOptions = KafkaLens.Grpc.FetchOptions;
using GrpcMessage = KafkaLens.Grpc.Message;
using GrpcTopic = KafkaLens.Grpc.Topic;
using GrpcCluster = KafkaLens.Grpc.Cluster;
using ValidateConnectionRequest = KafkaLens.Grpc.ValidateConnectionRequest;
using ValidateConnectionResponse = KafkaLens.Grpc.ValidateConnectionResponse;
using GetClustersResponse = KafkaLens.Grpc.GetClustersResponse;
using GetTopicsRequest = KafkaLens.Grpc.GetTopicsRequest;
using GetTopicsResponse = KafkaLens.Grpc.GetTopicsResponse;

namespace KafkaLens.Clients.Tests;

public class GrpcClientTests
{
    private readonly GrpcClient client = new("TestClient", "http://localhost:50051");

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
    public void ToClusterModel_PreservesEnabledStateWithBackwardCompatibleDefault()
    {
        var disabled = new GrpcCluster
        {
            Id = "cluster-1",
            Name = "MyCluster",
            BootstrapServers = "localhost:9092",
            IsEnabled = false
        };
        var legacy = new GrpcCluster
        {
            Id = "cluster-2",
            Name = "Legacy",
            BootstrapServers = "localhost:9093"
        };

        Assert.False(InvokeStatic<KafkaCluster>("ToClusterModel", disabled).IsEnabled);
        Assert.True(InvokeStatic<KafkaCluster>("ToClusterModel", legacy).IsEnabled);
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
        var options = new FetchOptions(FetchPosition.Start, 50);

        var result = InvokeStatic<GrpcFetchOptions>("ToGrpcFetchOptions", options);

        Assert.Equal(50u, result.MaxCount);
    }

    [Fact]
    public void ToGrpcFetchOptions_OffsetPosition_SetsOffset()
    {
        var position = new FetchPosition(PositionType.Offset, 100);
        var options = new FetchOptions(position, 10);

        var result = InvokeStatic<GrpcFetchOptions>("ToGrpcFetchOptions", options);

        Assert.Equal(100ul, result.Start.Offset);
    }

    [Fact]
    public void ToGrpcFetchOptions_TimestampPosition_SetsTimestamp()
    {
        var position = new FetchPosition(PositionType.Timestamp, 1704067200000);
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
        var options = new FetchOptions(FetchPosition.Start, 10);

        await Assert.ThrowsAsync<NotImplementedException>(() =>
            client.GetMessagesAsync("cluster", "topic", options));
    }

    [Fact]
    public async Task GetMessagesAsync_Partition_ThrowsNotImplementedException()
    {
        var options = new FetchOptions(FetchPosition.Start, 10);

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

    #region ValidateConnectionWithDetailsAsync Fallback

    private static AsyncUnaryCall<T> CreateCall<T>(T response) =>
        new(Task.FromResult(response), Task.FromResult(new Metadata()), () => Status.DefaultSuccess, () => new Metadata(), () => { });

    private static AsyncUnaryCall<T> CreateFailedCall<T>(StatusCode code, string detail = "Error") =>
        new(Task.FromException<T>(new RpcException(new Status(code, detail))), Task.FromResult(new Metadata()), () => new Status(code, detail), () => new Metadata(), () => { });

    [Fact]
    public async Task ValidateConnection_WhenUnimplemented_FallsBackToClusters_MatchingConnected()
    {
        var mockApi = NSubstitute.Substitute.For<KafkaLens.Grpc.KafkaApi.KafkaApiClient>();
        mockApi.ValidateConnectionAsync(Arg.Any<ValidateConnectionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateFailedCall<ValidateConnectionResponse>(StatusCode.Unimplemented));

        var clusters = new GetClustersResponse();
        clusters.Clusters.Add(new GrpcCluster
        {
            Id = "c1",
            Name = "Cluster1",
            BootstrapServers = "localhost:9092",
            IsConnected = true
        });
        mockApi.GetAllClustersAsync(Arg.Any<Empty>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateCall(clusters));

        var grpcClient = new GrpcClient("Test", "http://localhost:50051", mockApi);
        var result = await grpcClient.ValidateConnectionWithDetailsAsync("localhost:9092", System.Threading.CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ValidateConnection_WhenUnimplemented_FallsBackToClusters_MatchingDisconnected()
    {
        var mockApi = NSubstitute.Substitute.For<KafkaLens.Grpc.KafkaApi.KafkaApiClient>();
        mockApi.ValidateConnectionAsync(Arg.Any<ValidateConnectionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateFailedCall<ValidateConnectionResponse>(StatusCode.Unimplemented));

        var clusters = new GetClustersResponse();
        clusters.Clusters.Add(new GrpcCluster
        {
            Id = "c1",
            Name = "Cluster1",
            BootstrapServers = "localhost:9092",
            IsConnected = false
        });
        mockApi.GetAllClustersAsync(Arg.Any<Empty>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateCall(clusters));

        var grpcClient = new GrpcClient("Test", "http://localhost:50051", mockApi);
        var result = await grpcClient.ValidateConnectionWithDetailsAsync("localhost:9092", System.Threading.CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ValidateConnection_WhenUnimplemented_AndNoIsConnectedFlag_ProbesTopics_Success()
    {
        var mockApi = NSubstitute.Substitute.For<KafkaLens.Grpc.KafkaApi.KafkaApiClient>();
        mockApi.ValidateConnectionAsync(Arg.Any<ValidateConnectionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateFailedCall<ValidateConnectionResponse>(StatusCode.Unimplemented));

        var clusters = new GetClustersResponse();
        clusters.Clusters.Add(new GrpcCluster
        {
            Id = "c1",
            Name = "Cluster1",
            BootstrapServers = "localhost:9092"
        });
        mockApi.GetAllClustersAsync(Arg.Any<Empty>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateCall(clusters));

        var topicsResponse = new GetTopicsResponse();
        topicsResponse.Topics.Add(new GrpcTopic { Name = "test-topic", PartitionCount = 1 });
        mockApi.GetTopicsAsync(Arg.Any<GetTopicsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateCall(topicsResponse));

        var grpcClient = new GrpcClient("Test", "http://localhost:50051", mockApi);
        var result = await grpcClient.ValidateConnectionWithDetailsAsync("localhost:9092", System.Threading.CancellationToken.None);

        Assert.True(result.Succeeded);
    }

    [Fact]
    public async Task ValidateConnection_WhenUnimplemented_AndNoIsConnectedFlag_ProbesTopics_Failure()
    {
        var mockApi = NSubstitute.Substitute.For<KafkaLens.Grpc.KafkaApi.KafkaApiClient>();
        mockApi.ValidateConnectionAsync(Arg.Any<ValidateConnectionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateFailedCall<ValidateConnectionResponse>(StatusCode.Unimplemented));

        var clusters = new GetClustersResponse();
        clusters.Clusters.Add(new GrpcCluster
        {
            Id = "c1",
            Name = "Cluster1",
            BootstrapServers = "localhost:9092"
        });
        mockApi.GetAllClustersAsync(Arg.Any<Empty>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateCall(clusters));

        mockApi.GetTopicsAsync(Arg.Any<GetTopicsRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateFailedCall<GetTopicsResponse>(StatusCode.Unavailable, "Kafka broker unreachable"));

        var grpcClient = new GrpcClient("Test", "http://localhost:50051", mockApi);
        var result = await grpcClient.ValidateConnectionWithDetailsAsync("localhost:9092", System.Threading.CancellationToken.None);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task ValidateConnection_WhenUnimplemented_AndDaemonUnreachable_Fails()
    {
        var mockApi = NSubstitute.Substitute.For<KafkaLens.Grpc.KafkaApi.KafkaApiClient>();
        mockApi.ValidateConnectionAsync(Arg.Any<ValidateConnectionRequest>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateFailedCall<ValidateConnectionResponse>(StatusCode.Unimplemented));

        mockApi.GetAllClustersAsync(Arg.Any<Empty>(), Arg.Any<Metadata>(), Arg.Any<DateTime?>(), Arg.Any<System.Threading.CancellationToken>())
            .Returns(_ => CreateFailedCall<GetClustersResponse>(StatusCode.Unavailable, "gRPC server unavailable"));

        var grpcClient = new GrpcClient("Test", "http://localhost:50051", mockApi);
        var result = await grpcClient.ValidateConnectionWithDetailsAsync("localhost:9092", System.Threading.CancellationToken.None);

        Assert.False(result.Succeeded);
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