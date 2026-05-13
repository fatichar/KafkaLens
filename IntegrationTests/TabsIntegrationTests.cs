using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;

namespace IntegrationTests;

public class TabsIntegrationTests : IntegrationTestBase
{
    [AvaloniaFact]
    public async Task OpenMultipleTabs_ForSameCluster_ShouldHaveUniqueNames()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Multi Tab Cluster");

        MainViewModel.OpenClusterCommand.Execute(cluster.Id);
        await WaitUntilAsync(() => MainViewModel.OpenedClusters.Count == 1);
        MainViewModel.OpenClusterCommand.Execute(cluster.Id);
        await WaitUntilAsync(() => MainViewModel.OpenedClusters.Count == 2);

        MainViewModel.OpenedClusters[0].Name.Should().Be("Multi Tab Cluster");
        MainViewModel.OpenedClusters[1].Name.Should().Be("Multi Tab Cluster (1)");
    }

    [AvaloniaFact]
    public async Task CloseTab_ShouldRemoveFromOpenedClusters()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Close Tab Cluster");
        var openedCluster = await OpenClusterAsync(cluster);

        MainViewModel.CloseCurrentTabCommand.Execute(null);

        MainViewModel.OpenedClusters.Should().NotContain(openedCluster);
    }

    [AvaloniaFact]
    public async Task CloseAndReopenCluster_ShouldReuseBaseName()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Reusable Tab Name Cluster");
        var openedCluster = await OpenClusterAsync(cluster);

        MainViewModel.CloseCurrentTabCommand.Execute(null);
        await WaitUntilAsync(() => MainViewModel.OpenedClusters.Count == 0);
        var reopenedCluster = await OpenClusterAsync(cluster);

        reopenedCluster.Should().NotBeSameAs(openedCluster);
        reopenedCluster.Name.Should().Be("Reusable Tab Name Cluster");
    }
}
