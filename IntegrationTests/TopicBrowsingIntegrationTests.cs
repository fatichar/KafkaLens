using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;

namespace IntegrationTests;

public class TopicBrowsingIntegrationTests : IntegrationTestBase
{
    [AvaloniaFact]
    public async Task OpenCluster_ShouldLoadTopics()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Topic Test Cluster");

        var openedCluster = await OpenClusterAsync(cluster);

        openedCluster.Topics.Should().NotBeEmpty();
        openedCluster.Children.Should().HaveCount(openedCluster.Topics.Count);
    }

    [AvaloniaFact]
    public async Task FilterTopics_ShouldShowMatchingTopicsOnly()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Filter Test Cluster");
        var openedCluster = await OpenClusterAsync(cluster);
        var expectedTopicName = openedCluster.Topics.First().Name;

        openedCluster.FilterText = expectedTopicName;

        openedCluster.Children.Should().ContainSingle(t => t.Name == expectedTopicName);
        openedCluster.Children.Should().HaveCount(openedCluster.Topics.Count(t =>
            t.Name.Contains(expectedTopicName, StringComparison.OrdinalIgnoreCase)));
    }

    [AvaloniaFact]
    public async Task OpenCluster_ShouldCreatePartitionChildrenForTopics()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Partition Test Cluster");

        var openedCluster = await OpenClusterAsync(cluster);

        openedCluster.Topics.Should().OnlyContain(topic => topic.Partitions.Count > 0);
        openedCluster.Topics.First().Children.Should().HaveCount(openedCluster.Topics.First().Partitions.Count);
    }
}
