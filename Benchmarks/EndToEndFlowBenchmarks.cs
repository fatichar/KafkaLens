using System;
using System.Linq;
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
///
/// <see cref="IterationSetup"/> pre-opens the benchmark cluster and stores the
/// resulting tab in <see cref="_openedCluster"/> so individual benchmark methods
/// that do not measure the open operation start with a fully-loaded tab and
/// contain no artificial sleep.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 2, iterationCount: 8)]
public class EndToEndFlowBenchmarks
{
    private static HeadlessSession? _session;
    private MainViewModel _mainVm = null!;
    private string _clusterId = null!;
    private BenchmarkKafkaClient _benchClient = null!;
    private OpenedClusterViewModel _openedCluster = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _session = HeadlessSession.Start();

        _session.Run(async () =>
        {
            _mainVm = _session.Services.GetRequiredService<MainViewModel>();
            _benchClient = (BenchmarkKafkaClient)_session.Services
                .GetRequiredService<KafkaLens.Shared.IKafkaLensClient>();

            // Register a cluster so LoadClusters() has something to discover.
            var cluster = new KafkaCluster(
                Guid.NewGuid().ToString(), "Benchmark Cluster", "localhost:9092");
            await _benchClient.AddClusterAsync(cluster);
            _clusterId = cluster.Id;

            await _mainVm.LoadClusters();
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
        _session!.Run(async () =>
        {
            foreach (var tab in _mainVm.OpenedClusters.ToList())
            {
                if (tab?.CloseCommand != null)
                    tab.CloseCommand.Execute(null);
            }

            // Pre-open the benchmark cluster so benchmarks that don't measure the
            // open operation start with a fully-loaded tab and need no setup delay.
            _mainVm.OpenClusterCommand.Execute(_clusterId);
            _openedCluster = _mainVm.OpenedClusters[^1];
            _openedCluster.IsCurrent = true;
            await WaitForTopicsAsync(_openedCluster);
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Yields the UI thread until <paramref name="vm"/>.Topics is non-empty or
    /// <paramref name="timeoutMs"/> elapses.  Replaces fixed <c>Task.Delay</c> waits
    /// so benchmark timings reflect actual work rather than unconditional sleep.
    /// </summary>
    private static async Task WaitForTopicsAsync(OpenedClusterViewModel vm, int timeoutMs = 5_000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (vm.Topics.Count == 0 && Environment.TickCount64 < deadline)
            await Task.Delay(1);
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
            var tab = _mainVm.OpenedClusters[^1];
            await WaitForTopicsAsync(tab);
            _ = tab.Topics.Count;
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
            _openedCluster.SelectedNode = _openedCluster.Topics[0];
            await Dispatcher.UIThread.InvokeAsync(
                static () => { }, DispatcherPriority.Background);
            _ = _openedCluster.CurrentMessages.Messages.Count;
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
            const int switchCount = 5;
            for (int i = 0; i < switchCount; i++)
            {
                _openedCluster.SelectedNode = _openedCluster.Topics[i % _openedCluster.Topics.Count];
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
            for (int i = 0; i < 4; i++)
                _mainVm.OpenClusterCommand.Execute(_clusterId);

            // Wait for all 4 newly opened tabs to have topics loaded.
            var newTabs = _mainVm.OpenedClusters
                .Skip(_mainVm.OpenedClusters.Count - 4)
                .ToList();
            foreach (var tab in newTabs)
                await WaitForTopicsAsync(tab);

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
            // First load
            _openedCluster.SelectedNode = _openedCluster.Topics[0];
            await Dispatcher.UIThread.InvokeAsync(
                static () => { }, DispatcherPriority.Background);

            // Refresh (simulates user clicking Refresh)
            _openedCluster.RefreshCommand.Execute(null);
            await Dispatcher.UIThread.InvokeAsync(
                static () => { }, DispatcherPriority.Background);
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

            _mainVm.OpenClusterCommand.Execute(cluster.Id);
            var openedCluster = _mainVm.OpenedClusters[^1];
            openedCluster.IsCurrent = true;
            await WaitForTopicsAsync(openedCluster);

            openedCluster.SelectedNode = openedCluster.Topics[0];
            await Dispatcher.UIThread.InvokeAsync(
                static () => { }, DispatcherPriority.Background);

            _ = openedCluster.CurrentMessages.Messages.Count;

            // Teardown: close the tab and remove the temporary cluster.
            if (openedCluster?.CloseCommand != null)
                openedCluster.CloseCommand.Execute(null);
            await _benchClient.RemoveClusterByIdAsync(cluster.Id);
        });
    }
}
