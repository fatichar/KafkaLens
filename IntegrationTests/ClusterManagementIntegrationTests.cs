using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using KafkaLens.ViewModels;

namespace IntegrationTests;

public class ClusterManagementIntegrationTests : IntegrationTestBase
{
    [AvaloniaFact]
    public async Task AddCluster_ShouldAppearInList()
    {
        await ResetStateAsync();
        var editClustersVm = CreateEditClustersViewModel();

        const string clusterName = "New Test Cluster";
        const string clusterAddress = "localhost:9092";

        await editClustersVm.AddClusterAsync(clusterName, clusterAddress);

        MainViewModel.Clusters.Should().Contain(c => c.Name == clusterName && c.Address == clusterAddress);
    }

    [AvaloniaFact]
    public async Task UpdateCluster_ShouldRefreshListEntry()
    {
        await ResetStateAsync();
        await AddClusterAsync("Cluster Before Edit");
        var editClustersVm = CreateEditClustersViewModel();
        var cluster = MainViewModel.Clusters.Single();

        await editClustersVm.UpdateClusterAsync(cluster, "Cluster After Edit", "localhost:9093");

        cluster.Name.Should().Be("Cluster After Edit");
        cluster.Address.Should().Be("localhost:9093");
        MainViewModel.Clusters.Should().ContainSingle(c => c.Name == "Cluster After Edit" && c.Address == "localhost:9093");
    }

    [AvaloniaFact]
    public async Task RemoveCluster_ShouldRemoveFromList()
    {
        await ResetStateAsync();
        await AddClusterAsync("Cluster To Remove");
        var editClustersVm = CreateEditClustersViewModel();
        var cluster = MainViewModel.Clusters.Single();

        editClustersVm.RemoveCluster(cluster);

        MainViewModel.Clusters.Should().NotContain(c => c.Id == cluster.Id);
        editClustersVm.Clusters.Should().NotContain(c => c.Id == cluster.Id);
    }

    private EditClustersViewModel CreateEditClustersViewModel() =>
        new(
            MainViewModel.Clusters,
            MainViewModel.ClusterInfoRepository,
            MainViewModel.ClientInfoRepository,
            MainViewModel.ClientFactory);
}
