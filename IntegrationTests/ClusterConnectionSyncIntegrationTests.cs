using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels;

namespace IntegrationTests;

/// <summary>
/// Verifies that connection status stays in sync between opened tabs (bound directly to the
/// live ClusterViewModel) and the Edit Clusters &amp; Clients dialog (bound to staged copies that
/// mirror live status via a PropertyChanged subscription), across connectivity up/down cycles
/// driven by the periodic health check.
/// </summary>
public class ClusterConnectionSyncIntegrationTests : IntegrationTestBase
{
    [AvaloniaFact]
    public async Task PeriodicCycle_ChecksBothDirectClusters_LogsIdentities_AndRecoversWithoutDiscovery()
    {
        await ResetStateAsync();
        var first = await AddClusterAsync("First", "first:9092");
        var second = await AddClusterAsync("Second", "second:9092");
        using var dialog = CreateEditClustersViewModel();
        FakeKafkaClient.ResetCalls();
        FakeKafkaClient.SetReachable(first.Address, false);
        FakeKafkaClient.SetReachable(second.Address, false);

        await MainViewModel.RefreshClustersForHealthCheckAsync();

        FakeKafkaClient.ValidationCalls.Should().BeEquivalentTo(new Dictionary<string, int>
        {
            [first.Address] = 1, [second.Address] = 1
        });
        MainViewModel.Clusters.Should().OnlyContain(c => c.Status == ConnectionState.Failed);
        dialog.Clusters.Should().OnlyContain(c => c.Status == ConnectionState.Failed);
        FakeKafkaClient.DiscoveryCalls.Should().Be(0);
        foreach (var cluster in new[] { first, second })
            MainViewModel.AppLogService.Entries.Should().Contain(e =>
                e.Message.Contains("Periodic connection check") &&
                e.Message.Contains(cluster.Name) && e.Message.Contains(cluster.Id));

        FakeKafkaClient.SetReachable(first.Address, true);
        FakeKafkaClient.SetReachable(second.Address, true);
        await MainViewModel.RefreshClustersForHealthCheckAsync();

        MainViewModel.Clusters.Should().OnlyContain(c => c.Status == ConnectionState.Connected);
        dialog.Clusters.Should().OnlyContain(c => c.Status == ConnectionState.Connected);
    }

    [AvaloniaFact]
    public async Task MessageFailure_NeverPublishesConnectedOrRefetchesOnPeriodicCheck_ExplicitRetryRecovers()
    {
        await ResetStateAsync();
        var model = await AddClusterAsync();
        var tab = await OpenClusterAsync(model);
        var live = MainViewModel.Clusters.Single();
        using var dialog = CreateEditClustersViewModel();
        FakeKafkaClient.SetMessagesFailure(model.Id, true);
        tab.SelectedNode = tab.Topics.First();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        live.Status.Should().Be(ConnectionState.Failed);
        var observed = new List<ConnectionState>();
        System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == nameof(ClusterViewModel.Status)) observed.Add(live.Status);
        };
        live.PropertyChanged += handler;
        FakeKafkaClient.ResetCalls();
        try
        {
            FakeKafkaClient.SetMessagesFailure(model.Id, false);
            await MainViewModel.RefreshClustersForHealthCheckAsync();
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            observed.Should().NotContain(ConnectionState.Connected);
            FakeKafkaClient.MessagesCalls.Should().Be(0);
            live.Status.Should().Be(ConnectionState.Failed);
            dialog.Clusters.Single().Status.Should().Be(ConnectionState.Failed);
            tab.RefreshCommand.Execute(null);
            await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
            live.Status.Should().Be(ConnectionState.Connected);
            dialog.Clusters.Single().Status.Should().Be(ConnectionState.Connected);
        }
        finally
        {
            live.PropertyChanged -= handler;
        }
    }

    [AvaloniaFact]
    public async Task ClosedTab_DoesNotReactToLaterStatusChanges()
    {
        await ResetStateAsync();
        var model = await AddClusterAsync();
        var tab = await OpenClusterAsync(model);
        var live = MainViewModel.Clusters.Single();
        tab.SelectedNode = tab.Topics.First();
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        MainViewModel.CloseTab(tab);
        FakeKafkaClient.ResetCalls();
        live.Status = ConnectionState.Failed;
        live.Status = ConnectionState.Connected;
        await Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => { }, Avalonia.Threading.DispatcherPriority.Background);
        FakeKafkaClient.MessagesCalls.Should().Be(0);
        FakeKafkaClient.TopicsCalls.Should().Be(0);
    }


    [AvaloniaFact]
    public async Task PeriodicCheck_ClusterGoesUnreachable_ShouldMarkFailedInTabAndDialog()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Sync Cluster", "sync-cluster:9092");
        var opened = await OpenClusterAsync(cluster);
        var editVm = CreateEditClustersViewModel();

        var liveCluster = MainViewModel.Clusters.Single(c => c.Id == cluster.Id);
        liveCluster.Status.Should().Be(ConnectionState.Connected);
        editVm.Clusters.Single(c => c.Id == cluster.Id).Status.Should().Be(ConnectionState.Connected);
        opened.StatusColor.Should().Be("Green");

        FakeKafkaClient.SetReachable(cluster.Address, false);
        FakeKafkaClient.SetTopicsFailure(cluster.Id, true);

        await MainViewModel.RefreshClustersForHealthCheckAsync();
        await WaitUntilAsync(() => liveCluster.Status == ConnectionState.Failed);

        opened.StatusColor.Should().Be("Red");
        editVm.Clusters.Single(c => c.Id == cluster.Id).Status.Should().Be(ConnectionState.Failed);
        editVm.Clusters.Single(c => c.Id == cluster.Id).StatusColor.Should().Be("Red");
    }

    [AvaloniaFact]
    public async Task PeriodicCheck_ClusterBecomesReachableAgain_ShouldMarkConnectedInTabAndDialog()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Recovering Cluster", "recovering-cluster:9092");
        var opened = await OpenClusterAsync(cluster);
        var editVm = CreateEditClustersViewModel();
        var liveCluster = MainViewModel.Clusters.Single(c => c.Id == cluster.Id);

        FakeKafkaClient.SetReachable(cluster.Address, false);
        FakeKafkaClient.SetTopicsFailure(cluster.Id, true);
        await MainViewModel.RefreshClustersForHealthCheckAsync();
        await WaitUntilAsync(() => liveCluster.Status == ConnectionState.Failed);
        opened.StatusColor.Should().Be("Red");
        editVm.Clusters.Single(c => c.Id == cluster.Id).Status.Should().Be(ConnectionState.Failed);

        FakeKafkaClient.SetReachable(cluster.Address, true);
        FakeKafkaClient.SetTopicsFailure(cluster.Id, false);
        await MainViewModel.RefreshClustersForHealthCheckAsync();
        await WaitUntilAsync(() => liveCluster.Status == ConnectionState.Connected);

        opened.StatusColor.Should().Be("Green");
        editVm.Clusters.Single(c => c.Id == cluster.Id).Status.Should().Be(ConnectionState.Connected);
        editVm.Clusters.Single(c => c.Id == cluster.Id).StatusColor.Should().Be("Green");
    }

    [AvaloniaFact]
    public async Task DialogOpenedAfterStatusChange_ShouldReflectCurrentFailedStatus()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Late Open Cluster", "late-open-cluster:9092");
        await OpenClusterAsync(cluster);
        var liveCluster = MainViewModel.Clusters.Single(c => c.Id == cluster.Id);

        FakeKafkaClient.SetReachable(cluster.Address, false);
        FakeKafkaClient.SetTopicsFailure(cluster.Id, true);
        await MainViewModel.RefreshClustersForHealthCheckAsync();
        await WaitUntilAsync(() => liveCluster.Status == ConnectionState.Failed);

        // Dialog constructed AFTER the failure — must reflect current state immediately,
        // without performing any new connection check of its own.
        var editVm = CreateEditClustersViewModel();

        editVm.Clusters.Single(c => c.Id == cluster.Id).Status.Should().Be(ConnectionState.Failed);
        editVm.Clusters.Single(c => c.Id == cluster.Id).StatusColor.Should().Be("Red");
    }

    [AvaloniaFact]
    public async Task MultipleDirectClusters_OneGoesDown_OtherStaysUnaffected()
    {
        await ResetStateAsync();
        var stableCluster = await AddClusterAsync("Stable Cluster", "stable-cluster:9092");
        var failingCluster = await AddClusterAsync("Failing Cluster", "failing-cluster:9092");
        var stableOpened = await OpenClusterAsync(stableCluster);
        var failingOpened = await OpenClusterAsync(failingCluster);
        var editVm = CreateEditClustersViewModel();
        var liveFailing = MainViewModel.Clusters.Single(c => c.Id == failingCluster.Id);

        FakeKafkaClient.SetReachable(failingCluster.Address, false);
        FakeKafkaClient.SetTopicsFailure(failingCluster.Id, true);
        await MainViewModel.RefreshClustersForHealthCheckAsync();
        await WaitUntilAsync(() => liveFailing.Status == ConnectionState.Failed);

        failingOpened.StatusColor.Should().Be("Red");
        stableOpened.StatusColor.Should().Be("Green");
        editVm.Clusters.Single(c => c.Id == failingCluster.Id).Status.Should().Be(ConnectionState.Failed);
        editVm.Clusters.Single(c => c.Id == stableCluster.Id).Status.Should().Be(ConnectionState.Connected);
    }

    /// <summary>
    /// A failed topic-list operation is stronger evidence than broker validation alone.
    /// Periodic validation must re-run the failed topic-list operation before it can publish
    /// recovery, and the cluster must remain failed while that operation is still unavailable.
    /// </summary>
    [AvaloniaFact]
    public async Task PeriodicCheck_TopicsListStillFailing_ShouldRemainFailedAfterTargetedRevalidation()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Flaky Cluster", "flaky-cluster:9092");
        var opened = await OpenClusterAsync(cluster);
        var liveCluster = MainViewModel.Clusters.Single(c => c.Id == cluster.Id);
        liveCluster.Status.Should().Be(ConnectionState.Connected);

        // Simulate: broker TCP reachability is fine, but the topic-list fetch itself fails
        // (e.g. a stale cached validation result vs. a real per-request broker error).
        FakeKafkaClient.SetTopicsFailure(cluster.Id, true);
        await liveCluster.EnsureTopicsLoadedAsync(forceRefresh: true);
        await WaitUntilAsync(() => liveCluster.Status == ConnectionState.Failed);
        opened.StatusColor.Should().Be("Red");
        liveCluster.TopicLoadState.Should().Be(TopicLoadState.Failed);

        // Broker validation remains successful, but the health check must revalidate the
        // failed topic-list operation before changing the authoritative status.
        await MainViewModel.RefreshClustersForHealthCheckAsync();
        await WaitUntilAsync(() => liveCluster.TopicLoadState == TopicLoadState.Failed
            && liveCluster.Status == ConnectionState.Failed);

        liveCluster.Status.Should().Be(ConnectionState.Failed);
        opened.StatusColor.Should().Be("Red");
    }

    /// <summary>
    /// Pins down the reported false-positive: a successful broker validation must not erase a
    /// failed message operation while its topic list remains loaded. Message-path recovery is
    /// authoritative only after an explicit retry of that same topic or partition succeeds.
    /// </summary>
    [AvaloniaFact]
    public async Task PeriodicCheck_MessageLevelFailureWithTopicsStillLoaded_ShouldNotFalselyRestoreConnected()
    {
        await ResetStateAsync();
        var cluster = await AddClusterAsync("Message Flaky Cluster", "message-flaky-cluster:9092");
        var opened = await OpenClusterAsync(cluster);
        var liveCluster = MainViewModel.Clusters.Single(c => c.Id == cluster.Id);
        liveCluster.Status.Should().Be(ConnectionState.Connected);
        liveCluster.TopicLoadState.Should().Be(TopicLoadState.Loaded);

        var topic = opened.Topics.First();
        FakeKafkaClient.SetMessagesFailure(cluster.Id, true);

        // Selecting a Topic node on the current tab automatically triggers FetchMessages()
        // (see OpenedClusterViewModel.SelectedNode's setter) — no need to invoke a command.
        opened.SelectedNode = topic;

        await WaitUntilAsync(() => liveCluster.Status == ConnectionState.Failed);
        opened.StatusColor.Should().Be("Red");
        liveCluster.TopicLoadState.Should().Be(TopicLoadState.Loaded,
            "the topic LIST fetch never failed — only a specific topic's messages did");

        // Broker is reachable the whole time; only message-level access for this topic is
        // broken, and the topic-list-based re-verification guard won't catch that.
        await MainViewModel.RefreshClustersForHealthCheckAsync();
        await Task.Delay(50);

        liveCluster.Status.Should().Be(ConnectionState.Failed,
            "a periodic health check must not clear a real message-fetch failure based on " +
            "broker reachability alone");
        opened.StatusColor.Should().Be("Red");
    }
}
