using AutoFixture;
using AutoFixture.AutoNSubstitute;
using FluentAssertions;
using Google.Protobuf.WellKnownTypes;
using GrpcApi.Services;
using KafkaLens.Grpc;
using KafkaLens.Shared;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Models = KafkaLens.Shared.Models;

namespace KafkaLens.GrpcApi.Tests.Services;

public class KafkaServiceTests
{
    private readonly IFixture _fixture;
    private readonly IKafkaLensClient _kafkaLensClient;
    private readonly ILogger<KafkaService> _logger;
    private readonly KafkaService _sut;

    public KafkaServiceTests()
    {
        _fixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _kafkaLensClient = _fixture.Freeze<IKafkaLensClient>();
        _logger = _fixture.Freeze<ILogger<KafkaService>>();
        _sut = new KafkaService(_logger, _kafkaLensClient);
    }

    [Fact]
    public async Task GetAllClusters_ShouldReturnClusters()
    {
        // Arrange
        var clusters = _fixture.CreateMany<Models.KafkaCluster>(3).ToList();
        _kafkaLensClient.GetAllClustersAsync().Returns(clusters);

        // Act
        var response = await _sut.GetAllClusters(new Empty(), null!);

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
        var request = _fixture.Create<AddClusterRequest>();
        var cluster = _fixture.Create<Models.KafkaCluster>();
        _kafkaLensClient.AddAsync(Arg.Any<Models.NewKafkaCluster>()).Returns(cluster);

        // Act
        var response = await _sut.AddCluster(request, null!);

        // Assert
        response.Id.Should().Be(cluster.Id);
        response.Name.Should().Be(cluster.Name);
        response.BootstrapServers.Should().Be(cluster.Address);
        await _kafkaLensClient.Received(1).AddAsync(Arg.Is<Models.NewKafkaCluster>(c =>
            c.Name == request.Name && c.Address == request.BootstrapServers));
    }

    [Fact]
    public async Task GetTopics_ShouldReturnTopics()
    {
        // Arrange
        var request = _fixture.Create<GetTopicsRequest>();
        var topics = _fixture.CreateMany<Models.Topic>(3).ToList();
        _kafkaLensClient.GetTopicsAsync(request.ClusterId).Returns(topics);

        // Act
        var response = await _sut.GetTopics(request, null!);

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
        var request = _fixture.Create<RemoveClusterRequest>();

        // Act
        await _sut.RemoveCluster(request, null!);

        // Assert
        await _kafkaLensClient.Received(1).RemoveClusterByIdAsync(request.ClusterId);
    }
}
