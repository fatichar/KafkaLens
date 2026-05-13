using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;

namespace IntegrationTests;

public class MessageBrowsingIntegrationTests : IntegrationTestBase
{
    [AvaloniaFact]
    public async Task SelectTopic_ShouldLoadMessages()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Message Test Cluster");
        var openedCluster = await OpenClusterAsync(cluster);
        openedCluster.IsCurrent = true;
        var topic = openedCluster.Topics.First();

        openedCluster.SelectedNode = topic;

        await WaitUntilAsync(() => openedCluster.CurrentMessages.Messages.Count > 0);
        openedCluster.CurrentMessages.Messages.Should().OnlyContain(m => m.Topic == topic.Name);
    }

    [AvaloniaFact]
    public async Task SelectTopic_ShouldRespectFetchCount()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Fetch Count Cluster");
        var openedCluster = await OpenClusterAsync(cluster);
        openedCluster.IsCurrent = true;
        openedCluster.FetchCount = 5;

        openedCluster.SelectedNode = openedCluster.Topics.First();

        await WaitUntilAsync(() => openedCluster.CurrentMessages.Messages.Count == 5);
        openedCluster.CurrentMessages.Messages.Should().HaveCount(5);
    }

    [AvaloniaFact]
    public async Task SelectPartition_ShouldLoadMessagesForSelectedPartition()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Partition Message Cluster");
        var openedCluster = await OpenClusterAsync(cluster);
        openedCluster.IsCurrent = true;
        var partition = openedCluster.Topics.First().Partitions.First();

        openedCluster.SelectedNode = partition;

        await WaitUntilAsync(() => openedCluster.CurrentMessages.Messages.Count > 0);
        openedCluster.CurrentMessages.Messages.Should().OnlyContain(m =>
            m.Topic == partition.TopicName && m.Partition == partition.Id);
    }
}
