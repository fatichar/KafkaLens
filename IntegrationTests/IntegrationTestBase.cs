using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using FluentAssertions;
using IntegrationTests.Fakes;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace IntegrationTests;

public abstract class IntegrationTestBase
{
    protected TestApp App => (TestApp)AvaloniaApp.App.Current;
    protected MainViewModel MainViewModel => App.Services!.GetRequiredService<MainViewModel>();
    protected FakeKafkaClient FakeKafkaClient => (FakeKafkaClient)App.Services!.GetRequiredService<KafkaLens.Shared.IKafkaLensClient>();

    protected async Task ResetStateAsync()
    {
        await MainViewModel.LoadClusters();
        MainViewModel.OpenedClusters.Clear();
        MainViewModel.Clusters.Clear();
        MainViewModel.ClusterInfoRepository.DeleteAll();
        FakeKafkaClient.Reset();
    }

    protected async Task<KafkaCluster> AddClusterAsync(string name = "Test Cluster", string address = "localhost:9092")
    {
        var cluster = new KafkaCluster(Guid.NewGuid().ToString(), name, address);
        await FakeKafkaClient.AddClusterAsync(cluster);
        await MainViewModel.LoadClusters();
        await WaitUntilAsync(() => MainViewModel.Clusters.Any(c => c.Id == cluster.Id));
        return cluster;
    }

    protected async Task<OpenedClusterViewModel> OpenClusterAsync(KafkaCluster cluster)
    {
        MainViewModel.OpenClusterCommand.Execute(cluster.Id);

        await WaitUntilAsync(() =>
            MainViewModel.OpenedClusters.Any(c => c.ClusterId == cluster.Id && c.Topics.Count > 0));

        return MainViewModel.OpenedClusters.Last(c => c.ClusterId == cluster.Id);
    }

    /// <summary>
    /// Wires an EditClustersViewModel the same way MainView.axaml.cs does in production
    /// (tab-close/confirm delegates included), so tests exercise the real integration path
    /// rather than a partially-wired instance.
    /// </summary>
    protected EditClustersViewModel CreateEditClustersViewModel(bool confirmDisable = true) =>
        new(
            MainViewModel.Clusters,
            MainViewModel.ClusterInfoRepository,
            MainViewModel.ClientInfoRepository,
            MainViewModel.ClientFactory,
            MainViewModel.RefreshClustersForClientAsync)
        {
            HasOpenTabsForCluster = MainViewModel.HasOpenTabsForCluster,
            HasOpenTabsForClient = MainViewModel.HasOpenTabsForClient,
            CloseTabsForCluster = MainViewModel.CloseTabsForCluster,
            CloseTabsForClient = MainViewModel.CloseTabsForClient,
            ConfirmDisableCluster = _ => Task.FromResult(confirmDisable),
            ConfirmDisableClient = _ => Task.FromResult(confirmDisable),
            AvailabilityChanged = MainViewModel.RefreshAvailability
        };

    protected async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 5000)
    {
        var stopwatch = Stopwatch.StartNew();
        Exception? lastException = null;

        while (stopwatch.ElapsedMilliseconds < timeoutMs)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            try
            {
                if (condition())
                {
                    return;
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(20);
        }

        if (lastException != null)
        {
            throw new TimeoutException("Timed out waiting for integration test condition.", lastException);
        }

        condition().Should().BeTrue("the integration test condition should complete before the timeout");
    }
}
