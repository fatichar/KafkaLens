using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using KafkaLens.Shared.Models;

namespace Benchmarks;

/// <summary>
/// Validates the concurrent-fetch scenario described in
/// <c>docs/concurrent-fetch-plan.md</c>: multiple tabs fetching from the same cluster
/// simultaneously.
///
/// These benchmarks directly surface thread-safety regressions – if concurrent access
/// produces exceptions or deadlocks the benchmark will fail, providing a regression gate
/// in addition to a latency signal.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class ConcurrentFetchBenchmarks
{
    private FakeSyncKafkaClient _client = null!;
    private FetchOptions _opts = null!;

    /// <summary>Number of concurrent fetch tasks to run.</summary>
    [Params(1, 4, 8, 16)]
    public int Concurrency;

    [GlobalSetup]
    public void Setup()
    {
        _client = new FakeSyncKafkaClient(topicsPerCluster: 10, partitionsPerTopic: 4);
        _opts = new FetchOptions(FetchPosition.End, limit: 100);
    }

    // ── Topic-level concurrent fetch ──────────────────────────────────────────

    /// <summary>
    /// Simulates <see cref="Concurrency"/> tabs all requesting the same topic
    /// simultaneously.  Each call is independent (GetMessagesAsync is stateless).
    /// </summary>
    [Benchmark(Description = "Concurrent GetMessagesAsync – same topic")]
    public Task ConcurrentFetch_SameTopic()
    {
        var tasks = Enumerable.Range(0, Concurrency)
            .Select(_ => _client.GetMessagesAsync(
                _client.DefaultClusterId, "benchmark-topic-0000", _opts));
        return Task.WhenAll(tasks);
    }

    /// <summary>
    /// Simulates each tab fetching a different topic concurrently – the common
    /// "power user with multiple tabs open" scenario.
    /// </summary>
    [Benchmark(Description = "Concurrent GetMessagesAsync – different topics")]
    public Task ConcurrentFetch_DifferentTopics()
    {
        var tasks = Enumerable.Range(0, Concurrency)
            .Select(i => _client.GetMessagesAsync(
                _client.DefaultClusterId, $"benchmark-topic-{i % 10:D4}", _opts));
        return Task.WhenAll(tasks);
    }

    // ── Partition-level concurrent fetch ──────────────────────────────────────

    [Benchmark(Description = "Concurrent GetMessagesAsync – different partitions")]
    public Task ConcurrentFetch_DifferentPartitions()
    {
        var tasks = Enumerable.Range(0, Concurrency)
            .Select(i => _client.GetMessagesAsync(
                _client.DefaultClusterId, "benchmark-topic-0000",
                partition: i % 4, _opts));
        return Task.WhenAll(tasks);
    }

    // ── Mixed topic + partition ───────────────────────────────────────────────

    [Benchmark(Description = "Concurrent mixed topic & partition fetches")]
    public Task ConcurrentFetch_Mixed()
    {
        var tasks = Enumerable.Range(0, Concurrency).Select(i =>
        {
            if (i % 2 == 0)
                return _client.GetMessagesAsync(
                    _client.DefaultClusterId, $"benchmark-topic-{i % 10:D4}", _opts);
            else
                return _client.GetMessagesAsync(
                    _client.DefaultClusterId, "benchmark-topic-0000",
                    partition: i % 4, _opts);
        });
        return Task.WhenAll(tasks);
    }

    // ── Topic listing under concurrent fetch pressure ─────────────────────────

    [Benchmark(Description = "Concurrent GetTopicsAsync + GetMessagesAsync")]
    public Task ConcurrentFetch_TopicsAndMessages()
    {
        var fetchTasks = Enumerable.Range(0, Concurrency)
            .Select(_ => _client.GetMessagesAsync(
                _client.DefaultClusterId, "benchmark-topic-0000", _opts))
            .Cast<Task>();

        var topicTask = _client.GetTopicsAsync(_client.DefaultClusterId);

        return Task.WhenAll(fetchTasks.Append(topicTask));
    }
}
