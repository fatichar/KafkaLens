using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bogus;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;

namespace Benchmarks.Infrastructure;

/// <summary>
/// A synchronous, in-memory Kafka client for benchmarks that do not need the Avalonia
/// dispatcher.  <see cref="GetMessagesAsync"/> returns fake messages immediately;
/// <see cref="GetMessageStream"/> fills the stream before returning so callers that
/// attach handlers afterwards (e.g. OpenedClusterViewModel) must use the async variant.
/// </summary>
public sealed class FakeSyncKafkaClient : IKafkaLensClient
{
    private readonly Faker _faker = new();
    private readonly Dictionary<string, List<Topic>> _topicsByCluster = new();
    private readonly Dictionary<string, KafkaCluster> _clusters = new();
    private readonly int _topicsPerCluster;
    private readonly int _partitionsPerTopic;

    public string Name => "Benchmark";
    public bool CanEditClusters => true;
    public bool CanSaveMessages => false;

    public FakeSyncKafkaClient(int topicsPerCluster = 20, int partitionsPerTopic = 3)
    {
        _topicsPerCluster = topicsPerCluster;
        _partitionsPerTopic = partitionsPerTopic;

        // Pre-seed one cluster so benchmarks can start fetching immediately.
        var cluster = new KafkaCluster("bench-cluster-1", "Benchmark Cluster", "localhost:9092");
        _clusters[cluster.Id] = cluster;
        _topicsByCluster[cluster.Id] = GenerateTopics(_topicsPerCluster, _partitionsPerTopic);
    }

    public string DefaultClusterId => "bench-cluster-1";

    // ── Cluster CRUD ──────────────────────────────────────────────────────────

    public Task<bool> ValidateConnectionAsync(string bootstrapServers) =>
        Task.FromResult(true);

    public Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        var cluster = new KafkaCluster(Guid.NewGuid().ToString(), newCluster.Name, newCluster.Address);
        _clusters[cluster.Id] = cluster;
        _topicsByCluster[cluster.Id] = GenerateTopics(_topicsPerCluster, _partitionsPerTopic);
        return Task.FromResult(cluster);
    }

    public Task<IEnumerable<KafkaCluster>> GetAllClustersAsync() =>
        Task.FromResult(_clusters.Values.AsEnumerable());

    public Task<KafkaCluster> GetClusterByIdAsync(string clusterId) =>
        Task.FromResult(_clusters[clusterId]);

    public Task<KafkaCluster> GetClusterByNameAsync(string name) =>
        Task.FromResult(_clusters.Values.First(c => c.Name == name));

    public Task<KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update)
    {
        var cluster = _clusters[clusterId];
        var updated = new KafkaCluster(clusterId, update.Name, update.Address);
        _clusters[clusterId] = updated;
        return Task.FromResult(updated);
    }

    public Task RemoveClusterByIdAsync(string id)
    {
        _clusters.Remove(id);
        _topicsByCluster.Remove(id);
        return Task.CompletedTask;
    }

    // ── Topic / Message fetch ─────────────────────────────────────────────────

    public Task<IList<Topic>> GetTopicsAsync(string clusterId)
    {
        if (_topicsByCluster.TryGetValue(clusterId, out var topics))
            return Task.FromResult<IList<Topic>>(topics);
        return Task.FromResult<IList<Topic>>(new List<Topic>());
    }

    public Task<List<Message>> GetMessagesAsync(
        string clusterId, string topic, FetchOptions options,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GenerateMessages(options.Limit));

    public Task<List<Message>> GetMessagesAsync(
        string clusterId, string topic, int partition, FetchOptions options,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(GenerateMessages(options.Limit, partition));

    /// <summary>
    /// Fills the stream synchronously before returning.  Only suitable for benchmarks
    /// that attach event handlers before calling this method (i.e. not the ViewModel layer).
    /// </summary>
    public MessageStream GetMessageStream(
        string clusterId, string topic, FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var stream = new MessageStream();
        var messages = GenerateMessages(options.Limit);
        stream.Messages.AddRange(messages);
        stream.HasMore = false;
        return stream;
    }

    public MessageStream GetMessageStream(
        string clusterId, string topic, int partition, FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var stream = new MessageStream();
        var messages = GenerateMessages(options.Limit, partition);
        stream.Messages.AddRange(messages);
        stream.HasMore = false;
        return stream;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<Topic> GenerateTopics(int count, int partitions) =>
        Enumerable.Range(0, count)
            .Select(i => new Topic($"benchmark-topic-{i:D4}", partitions))
            .ToList();

    internal static List<Message> GenerateMessages(int count, int partition = -1)
    {
        if (count <= 0) count = 10;
        var rng = new Random(42); // deterministic seed
        var messages = new List<Message>(count);
        for (int i = 0; i < count; i++)
        {
            var key = new byte[8];
            var value = new byte[256];
            rng.NextBytes(key);
            rng.NextBytes(value);
            var msg = new Message(
                DateTimeOffset.UtcNow.AddSeconds(-i).ToUnixTimeMilliseconds(),
                new Dictionary<string, byte[]>(),
                key,
                value)
            {
                Partition = partition >= 0 ? partition : i % 4,
                Offset = 10_000 + i
            };
            messages.Add(msg);
        }
        return messages;
    }
}
