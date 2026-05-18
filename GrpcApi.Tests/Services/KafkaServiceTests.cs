using AutoFixture;
using AutoFixture.AutoNSubstitute;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcApi.Config;
using GrpcApi.Services;
using KafkaLens.Grpc;
using KafkaLens.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Runtime.CompilerServices;
using Xunit;
using Models = KafkaLens.Shared.Models;
using GrpcMessage = KafkaLens.Grpc.Message;

namespace KafkaLens.GrpcApi.Tests.Services;

public class KafkaServiceTests
{
    private readonly IFixture fixture;
    private readonly IKafkaLensClient kafkaLensClient;
    private readonly KafkaService sut;

    public KafkaServiceTests()
    {
        fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        kafkaLensClient = fixture.Freeze<IKafkaLensClient>();
        var logger = fixture.Freeze<ILogger<KafkaService>>();
        sut = new KafkaService(logger, kafkaLensClient);
    }

    [Fact]
    public async Task GetAllClusters_ShouldReturnClusters()
    {
        // Arrange
        var clusters = fixture.CreateMany<Models.KafkaCluster>(3).ToList();
        kafkaLensClient.GetAllClustersAsync().Returns(clusters);

        // Act
        var response = await sut.GetAllClusters(new Empty(), null!);

        // Assert
        response.Clusters.Should().HaveCount(clusters.Count);
        for (int i = 0; i < clusters.Count; i++)
        {
            response.Clusters[i].Id.Should().Be(clusters[i].Id);
            response.Clusters[i].Name.Should().Be(clusters[i].Name);
            response.Clusters[i].BootstrapServers.Should().Be(clusters[i].Address);
        }
    }

    [Fact]
    public async Task AddCluster_ShouldAddAndReturnCluster()
    {
        // Arrange
        var request = fixture.Create<AddClusterRequest>();
        var cluster = fixture.Create<Models.KafkaCluster>();
        kafkaLensClient.AddAsync(Arg.Any<Models.NewKafkaCluster>()).Returns(cluster);

        // Act
        var response = await sut.AddCluster(request, null!);

        // Assert
        response.Id.Should().Be(cluster.Id);
        response.Name.Should().Be(cluster.Name);
        response.BootstrapServers.Should().Be(cluster.Address);
        await kafkaLensClient.Received(1).AddAsync(Arg.Is<Models.NewKafkaCluster>(c =>
            c.Name == request.Name && c.Address == request.BootstrapServers));
    }

    [Fact]
    public async Task GetTopics_ShouldReturnTopics()
    {
        // Arrange
        var request = fixture.Create<GetTopicsRequest>();
        var topics = fixture.CreateMany<Models.Topic>(3).ToList();
        kafkaLensClient.GetTopicsAsync(request.ClusterId).Returns(topics);

        // Act
        var response = await sut.GetTopics(request, null!);

        // Assert
        response.Topics.Should().HaveCount(topics.Count);
        for (int i = 0; i < topics.Count; i++)
        {
            response.Topics[i].Name.Should().Be(topics[i].Name);
            response.Topics[i].PartitionCount.Should().Be((uint)topics[i].PartitionCount);
        }
    }

    [Fact]
    public async Task RemoveCluster_ShouldCallRemove()
    {
        // Arrange
        var request = fixture.Create<RemoveClusterRequest>();

        // Act
        await sut.RemoveCluster(request, null!);

        // Assert
        await kafkaLensClient.Received(1).RemoveClusterByIdAsync(request.ClusterId);
    }

    [Fact]
    public async Task GetTopicMessages_WhenRequestExceedsServerLimit_ThrowsResourceExhausted()
    {
        // Arrange
        var streamingClient = Substitute.For<IKafkaLensClient, IStreamingKafkaLensClient>();
        var config = new GrpcFetchConfig { MaxMessagesPerRequest = 1 };
        var service = CreateService(streamingClient, config);
        var request = CreateTopicMessagesRequest(maxCount: 2);
        var writer = CreateWriter();
        var context = CreateContext();

        // Act
        var exception = await Assert.ThrowsAsync<RpcException>(() => service.GetTopicMessages(request, writer, context));

        // Assert
        exception.StatusCode.Should().Be(StatusCode.ResourceExhausted);
        ((IStreamingKafkaLensClient)streamingClient)
            .DidNotReceive()
            .StreamMessagesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Models.FetchOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetTopicMessages_WhenLimiterIsBusy_ThrowsResourceExhausted()
    {
        // Arrange
        var streamingClient = Substitute.For<IKafkaLensClient, IStreamingKafkaLensClient>();
        var config = new GrpcFetchConfig
        {
            MaxMessagesPerRequest = 10,
            MaxConcurrentFetches = 1,
            QueueTimeoutMs = 1
        };
        var limiter = new GrpcFetchLimiter(config);
        var service = CreateService(streamingClient, config, limiter);
        var request = CreateTopicMessagesRequest(maxCount: 1);
        var writer = CreateWriter();
        var context = CreateContext();

        var acquired = await limiter.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None);
        acquired.Should().BeTrue();

        try
        {
            // Act
            var exception = await Assert.ThrowsAsync<RpcException>(() => service.GetTopicMessages(request, writer, context));

            // Assert
            exception.StatusCode.Should().Be(StatusCode.ResourceExhausted);
        }
        finally
        {
            limiter.Release();
        }
    }

    [Fact]
    public async Task GetTopicMessages_AfterSuccessfulFetch_ReleasesLimiterSlot()
    {
        // Arrange
        var streamingClient = Substitute.For<IKafkaLensClient, IStreamingKafkaLensClient>();
        var streaming = (IStreamingKafkaLensClient)streamingClient;
        streaming.StreamMessagesAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Models.FetchOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ => CreateMessages(1));

        var config = new GrpcFetchConfig
        {
            MaxMessagesPerRequest = 10,
            MaxConcurrentFetches = 1,
            QueueTimeoutMs = 1_000
        };
        var limiter = new GrpcFetchLimiter(config);
        var service = CreateService(streamingClient, config, limiter);
        var request = CreateTopicMessagesRequest(maxCount: 1);
        var writer = CreateWriter();
        var context = CreateContext();

        // Act
        await service.GetTopicMessages(request, writer, context);
        var acquiredAfterFetch = await limiter.TryAcquireAsync(TimeSpan.Zero, CancellationToken.None);

        // Assert
        acquiredAfterFetch.Should().BeTrue();
        await writer.Received(1).WriteAsync(Arg.Any<GrpcMessage>());
        limiter.Release();
    }

    private KafkaService CreateService(
        IKafkaLensClient client,
        GrpcFetchConfig? config = null,
        GrpcFetchLimiter? limiter = null)
    {
        return new KafkaService(
            fixture.Freeze<ILogger<KafkaService>>(),
            client,
            config ?? new GrpcFetchConfig(),
            limiter);
    }

    private static GetTopicMessagesRequest CreateTopicMessagesRequest(uint maxCount)
    {
        return new GetTopicMessagesRequest
        {
            ClusterId = "cluster",
            TopicName = "topic",
            FetchOptions = new FetchOptions
            {
                MaxCount = maxCount,
                Start = new FetchPosition
                {
                    Offset = 0
                }
            }
        };
    }

    private static IServerStreamWriter<GrpcMessage> CreateWriter()
    {
        var writer = Substitute.For<IServerStreamWriter<GrpcMessage>>();
        writer.WriteAsync(Arg.Any<GrpcMessage>()).Returns(Task.CompletedTask);
        return writer;
    }

    private static ServerCallContext CreateContext(CancellationToken cancellationToken = default)
    {
        var context = Substitute.For<ServerCallContext>();
        context.CancellationToken.Returns(cancellationToken);
        return context;
    }

    private static async IAsyncEnumerable<Models.Message> CreateMessages(
        int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var i = 0; i < count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return new Models.Message(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), new Dictionary<string, byte[]>(), null, null)
            {
                Offset = i,
                Partition = 0
            };
            await Task.Yield();
        }
    }
}
