using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace Benchmarks;

/// <summary>
/// End-to-end ViewModel-layer benchmarks executed inside a real Avalonia headless
/// application.  These cover the flows users trigger most often:
///
/// <list type="bullet">
///   <item>Opening a cluster tab and loading its topics</item>
///   <item>Selecting a topic and receiving the first batch of messages</item>
///   <item>Switching between topics in a single tab</item>
///   <item>Opening multiple tabs for the same cluster (concurrent load)</item>
/// </list>
///
/// The Avalonia headless session is started once per process (GlobalSetup) so all
/// iterations share the same running dispatcher.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
public class EndToEndFlowBenchmarks
{
    private static HeadlessSession? _session;
    private MainViewModel _mainVm = null!;
    private string _clusterId = null!;
    private BenchmarkKafkaClient _benchClient = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _session = HeadlessSession.Start();

        _session.Run(async () =>
        {
            _mainVm = _session.Services.GetRequiredService<MainViewModel>();
            _benchClient = (BenchmarkKafkaClient)_session.Services
                .GetRequiredService<KafkaLens.Shared.IKafkaLensClient>();

            // Add a cluster to work with.
            var cluster = new KafkaCluster(
                Guid.NewGuid().ToString(), "Benchmark Cluster", "localhost:9092");
            await _benchClient.AddClusterAsync(cluster);
            _clusterId = cluster.Id;

            await _mainVm.LoadClusters();
            await Task.Delay(100); // allow UI thread to process the cluster update
        });
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _session?.Dispose();
        _session = null;
    }

    // ── Per-iteration reset ───────────────────────────────────────────────────

    [IterationSetup]
    public void IterationSetup()
    {
        _session!.Run(() =>
        {
            foreach (var tab in _mainVm.OpenedClusters.ToList())
                _mainVm.CloseTab(tab);
        });
    }

    // ── Benchmarks ────────────────────────────────────────────────────────────

    /// <summary>
    /// Measures the time from "Open Cluster" command execution to the moment all
    /// topics have been loaded and are visible in the ViewModel.
    /// </summary>
    [Benchmark(Description = "OpenCluster – topic list loaded")]
    public void OpenCluster_LoadTopics()
    {
        _session!.Run(async () =>
        {
            _mainVm.OpenClusterCommand.Execute(_clusterId);
            await Task.Delay(50);   // await first dispatcher post (status check)

            var openedCluster = _mainVm.OpenedClusters[^1];
            await Task.Delay(300);  // await topic fetch + UI update

            // Ensure topics are loaded before we finish measuring.
            _ = openedCluster.Topics.Count;
        });
    }

    /// <summary>
    /// Measures the time from selecting a topic to the first batch of messages
    /// appearing in <c>CurrentMessages</c>.
    /// </summary>
    [Benchmark(Description = "SelectTopic – first message batch received")]
    public void SelectTopic_ReceiveMessages()
    {
        _session!.Run(async () =>
        {
            _mainVm.OpenClusterCommand.Execute(_clusterId);
            await Task.Delay(50);
            var openedCluster = _mainVm.OpenedClusters[^1];
            openedCluster.IsCurrent = true;
            await Task.Delay(300); // topics loaded

            var topicVm = openedCluster.Topics[0];
            openedCluster.SelectedNode = topicVm;  // triggers FetchMessages()

            // Flush Normal-priority dispatcher work (LoadFakeMessages + UpdateMessages)
            // by posting at Background priority and awaiting it.
            await Dispatcher.UIThread.InvokeAsync(
                static () => { }, DispatcherPriority.Background);

            _ = openedCluster.CurrentMessages.Messages.Count;
        });
    }

    /// <summary>
    /// Measures the cost of switching between topics in an already-open tab.
    /// On each switch the previous fetch is cancelled and a new one starts.
    /// </summary>
    [Benchmark(Description = "TopicSwitch – cycle through 5 topics")]
    public void TopicSwitch_CycleTopics()
    {
        _session!.Run(async () =>
        {
            _mainVm.OpenClusterCommand.Execute(_clusterId);
            await Task.Delay(50);
            var openedCluster = _mainVm.OpenedClusters[^1];
            openedCluster.IsCurrent = true;
            await Task.Delay(300); // topics loaded

            const int switchCount = 5;
            for (int i = 0; i < switchCount; i++)
            {
                openedCluster.SelectedNode = openedCluster.Topics[i % openedCluster.Topics.Count];
                await Dispatcher.UIThread.InvokeAsync(
                    static () => { }, DispatcherPriority.Background);
            }
        });
    }

    /// <summary>
    /// Simulates multiple tabs opening the same cluster concurrently – the primary
    /// scenario from <c>docs/concurrent-fetch-plan.md</c>.
    /// </summary>
    [Benchmark(Description = "ConcurrentTabs – open 4 tabs for same cluster")]
    public void ConcurrentTabs_OpenFourTabs()
    {
        _session!.Run(async () =>
        {
            // Open 4 tabs in quick succession (sequential on the UI thread, but each
            // tab fetches topics asynchronously and in a real scenario would overlap).
            for (int i = 0; i < 4; i++)
                _mainVm.OpenClusterCommand.Execute(_clusterId);

            await Task.Delay(400); // allow all 4 tabs to load topics

            _ = _mainVm.OpenedClusters.Count;
        });
    }

    /// <summary>
    /// Measures message pagination: fetching the next batch (Refresh) on a topic
    /// that already has messages loaded.
    /// </summary>
    [Benchmark(Description = "RefreshMessages – reload current topic")]
    public void RefreshMessages_SameTopic()
    {
        _session!.Run(async () =>
        {
            _mainVm.OpenClusterCommand.Execute(_clusterId);
            await Task.Delay(50);
            var openedCluster = _mainVm.OpenedClusters[^1];
            openedCluster.IsCurrent = true;
            await Task.Delay(300);

            // First load
            openedCluster.SelectedNode = openedCluster.Topics[0];
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

            // Refresh (simulates user clicking Refresh)
            openedCluster.RefreshCommand.Execute(null);
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
        });
    }

    /// <summary>
    /// Measures the full add-cluster → open → load topics → fetch messages flow
    /// end to end.
    /// </summary>
    [Benchmark(Description = "FullFlow – add cluster → open → topics → messages")]
    public void FullFlow_AddOpenFetch()
    {
        _session!.Run(async () =>
        {
            var cluster = new KafkaCluster(
                Guid.NewGuid().ToString(), "Flow Cluster", "localhost:9092");
            await _benchClient.AddClusterAsync(cluster);
            await _mainVm.LoadClusters();
            await Task.Delay(100);

            _mainVm.OpenClusterCommand.Execute(cluster.Id);
            await Task.Delay(50);
            var openedCluster = _mainVm.OpenedClusters[^1];
            openedCluster.IsCurrent = true;
            await Task.Delay(300);

            openedCluster.SelectedNode = openedCluster.Topics[0];
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);

            _ = openedCluster.CurrentMessages.Messages.Count;

            // Teardown: close the tab and remove the temporary cluster.
            _mainVm.OpenedClusters.Remove(openedCluster);
            await _benchClient.RemoveClusterByIdAsync(cluster.Id);
        });
    }
}
