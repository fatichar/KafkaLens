using System.Collections.Generic;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Benchmarks.Infrastructure;
using KafkaLens.Shared.Models;

namespace Benchmarks;

/// <summary>
/// Measures the cost of fetching messages through the <see cref="IKafkaLensClient"/>
/// layer at varying batch sizes.  Exercises the full service pipeline from client call
/// through to the returned <see cref="List{T}"/>, without involving the ViewModel or
/// Avalonia dispatcher.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class MessageFetchBenchmarks
{
    private FakeSyncKafkaClient _client = null!;
    private FetchOptions _opts10 = null!;
    private FetchOptions _opts100 = null!;
    private FetchOptions _opts1000 = null!;
    private FetchOptions _opts10000 = null!;

    [GlobalSetup]
    public void Setup()
    {
        _client = new FakeSyncKafkaClient(topicsPerCluster: 5, partitionsPerTopic: 3);

        var start = FetchPosition.End;
        _opts10    = new FetchOptions(start, limit: 10);
        _opts100   = new FetchOptions(start, limit: 100);
        _opts1000  = new FetchOptions(start, limit: 1_000);
        _opts10000 = new FetchOptions(start, limit: 10_000);
    }

    // ── Topic-level fetch ─────────────────────────────────────────────────────

    [Benchmark(Description = "GetMessagesAsync – topic, 10 msgs")]
    public Task<List<Message>> GetMessages_Topic_10() =>
        _client.GetMessagesAsync(_client.DefaultClusterId, "benchmark-topic-0000", _opts10);

    [Benchmark(Description = "GetMessagesAsync – topic, 100 msgs")]
    public Task<List<Message>> GetMessages_Topic_100() =>
        _client.GetMessagesAsync(_client.DefaultClusterId, "benchmark-topic-0000", _opts100);

    [Benchmark(Description = "GetMessagesAsync – topic, 1 000 msgs")]
    public Task<List<Message>> GetMessages_Topic_1k() =>
        _client.GetMessagesAsync(_client.DefaultClusterId, "benchmark-topic-0000", _opts1000);

    [Benchmark(Description = "GetMessagesAsync – topic, 10 000 msgs")]
    public Task<List<Message>> GetMessages_Topic_10k() =>
        _client.GetMessagesAsync(_client.DefaultClusterId, "benchmark-topic-0000", _opts10000);

    // ── Partition-level fetch ─────────────────────────────────────────────────

    [Benchmark(Description = "GetMessagesAsync – partition, 100 msgs")]
    public Task<List<Message>> GetMessages_Partition_100() =>
        _client.GetMessagesAsync(_client.DefaultClusterId, "benchmark-topic-0000", partition: 0, _opts100);

    [Benchmark(Description = "GetMessagesAsync – partition, 1 000 msgs")]
    public Task<List<Message>> GetMessages_Partition_1k() =>
        _client.GetMessagesAsync(_client.DefaultClusterId, "benchmark-topic-0000", partition: 0, _opts1000);

    // ── MessageStream path (synchronous fill) ─────────────────────────────────

    [Benchmark(Description = "GetMessageStream – 100 msgs (sync fill)")]
    public MessageStream GetMessageStream_100()
    {
        var stream = _client.GetMessageStream(
            _client.DefaultClusterId, "benchmark-topic-0000", _opts100);
        return stream;
    }

    [Benchmark(Description = "GetMessageStream – 1 000 msgs (sync fill)")]
    public MessageStream GetMessageStream_1k()
    {
        var stream = _client.GetMessageStream(
            _client.DefaultClusterId, "benchmark-topic-0000", _opts1000);
        return stream;
    }
}
