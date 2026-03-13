using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using KafkaLens.Shared.Models;

namespace Benchmarks;

/// <summary>
/// Measures the cost of listing topics from a cluster at varying topic counts.
/// Reflects the "open cluster" action that users trigger when connecting to Kafka.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class TopicLoadBenchmarks
{
    private FakeSyncKafkaClient _client10 = null!;
    private FakeSyncKafkaClient _client50 = null!;
    private FakeSyncKafkaClient _client200 = null!;
    private FakeSyncKafkaClient _client1000 = null!;
    private FakeSyncKafkaClient _clientHighPartitions = null!;

    [GlobalSetup]
    public void Setup()
    {
        _client10             = new FakeSyncKafkaClient(topicsPerCluster: 10,   partitionsPerTopic: 3);
        _client50             = new FakeSyncKafkaClient(topicsPerCluster: 50,   partitionsPerTopic: 3);
        _client200            = new FakeSyncKafkaClient(topicsPerCluster: 200,  partitionsPerTopic: 3);
        _client1000           = new FakeSyncKafkaClient(topicsPerCluster: 1000, partitionsPerTopic: 3);
        _clientHighPartitions = new FakeSyncKafkaClient(topicsPerCluster: 50,   partitionsPerTopic: 20);
    }

    [Benchmark(Description = "GetTopicsAsync – 10 topics")]
    public Task<IList<Topic>> GetTopics_10() =>
        _client10.GetTopicsAsync(_client10.DefaultClusterId);

    [Benchmark(Description = "GetTopicsAsync – 50 topics")]
    public Task<IList<Topic>> GetTopics_50() =>
        _client50.GetTopicsAsync(_client50.DefaultClusterId);

    [Benchmark(Description = "GetTopicsAsync – 200 topics")]
    public Task<IList<Topic>> GetTopics_200() =>
        _client200.GetTopicsAsync(_client200.DefaultClusterId);

    [Benchmark(Description = "GetTopicsAsync – 1 000 topics")]
    public Task<IList<Topic>> GetTopics_1k() =>
        _client1000.GetTopicsAsync(_client1000.DefaultClusterId);

    // ── Repeated access (cache-hit simulation) ────────────────────────────────

    [Benchmark(Description = "GetTopicsAsync – 200 topics, repeated (warm)")]
    public async Task GetTopics_200_Repeated()
    {
        // Simulate a user refreshing the topic list 5 times in succession.
        for (int i = 0; i < 5; i++)
            await _client200.GetTopicsAsync(_client200.DefaultClusterId);
    }

    // ── Partition-heavy clusters ──────────────────────────────────────────────

    [Benchmark(Description = "GetTopicsAsync – 50 topics × 20 partitions")]
    public Task<IList<Topic>> GetTopics_50_HighPartitions() =>
        _clientHighPartitions.GetTopicsAsync(_clientHighPartitions.DefaultClusterId);
}
