using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;

namespace Benchmarks.Infrastructure;

/// <summary>
/// Fake Kafka client for ViewModel-level benchmarks that uses
/// <see cref="Dispatcher.UIThread"/> to defer stream filling, mirroring
/// how a real async client delivers messages after event handlers are attached.
/// </summary>
public sealed class BenchmarkKafkaClient : IKafkaLensClient
{
    private readonly IClusterInfoRepository _repo;
    private readonly Dictionary<string, List<Topic>> _topicsByCluster = new();

    // Must be "Local" so ClientFactory.LoadClientsAsync() preserves this client
    // when the in-memory ClientInfoRepository is empty (no persisted client entries).
    public string Name => "Local";
    public bool CanEditClusters => true;
    public bool CanSaveMessages => false;

    public BenchmarkKafkaClient(IClusterInfoRepository repo)
    {
        _repo = repo;
    }

    public Task<bool> ValidateConnectionAsync(string bootstrapServers) =>
        Task.FromResult(true);

    public Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        var info = _repo.Add(newCluster.Name, newCluster.Address);
        _topicsByCluster[info.Id] = GenerateTopics(info.Id);
        return Task.FromResult(new KafkaCluster(info.Id, info.Name, info.Address));
    }

    public Task<KafkaCluster> AddClusterAsync(KafkaCluster cluster)
    {
        var info = new KafkaLens.Shared.Entities.ClusterInfo(cluster.Id, cluster.Name, cluster.Address);
        _repo.Add(info);
        _topicsByCluster[cluster.Id] = GenerateTopics(cluster.Id);
        return Task.FromResult(cluster);
    }

    public Task<IEnumerable<KafkaCluster>> GetAllClustersAsync() =>
        Task.FromResult(_repo.GetAll().Values.Select(i => new KafkaCluster(i.Id, i.Name, i.Address)));

    public Task<KafkaCluster> GetClusterByIdAsync(string clusterId)
    {
        var info = _repo.GetById(clusterId);
        return Task.FromResult(new KafkaCluster(info.Id, info.Name, info.Address));
    }

    public Task<KafkaCluster> GetClusterByNameAsync(string name)
    {
        var info = _repo.GetAll().Values.First(c => c.Name == name);
        return Task.FromResult(new KafkaCluster(info.Id, info.Name, info.Address));
    }

    public Task<KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update)
    {
        var info = _repo.GetById(clusterId);
        info.Name = update.Name;
        info.Address = update.Address;
        _repo.Update(info);
        return Task.FromResult(new KafkaCluster(clusterId, update.Name, update.Address));
    }

    public Task RemoveClusterByIdAsync(string id)
    {
        _repo.Delete(id);
        _topicsByCluster.Remove(id);
        return Task.CompletedTask;
    }

    public Task<IList<Topic>> GetTopicsAsync(string clusterId)
    {
        if (_topicsByCluster.TryGetValue(clusterId, out var topics))
            return Task.FromResult<IList<Topic>>(topics);
        return Task.FromResult<IList<Topic>>(new List<Topic>());
    }

    public MessageStream GetMessageStream(
        string clusterId, string topic, FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var stream = new MessageStream();
        Dispatcher.UIThread.Post(() => FillStream(stream, options.Limit));
        return stream;
    }

    public Task<List<Message>> GetMessagesAsync(
        string clusterId, string topic, FetchOptions options,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(FakeSyncKafkaClient.GenerateMessages(options.Limit));

    public MessageStream GetMessageStream(
        string clusterId, string topic, int partition, FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var stream = new MessageStream();
        Dispatcher.UIThread.Post(() => FillStream(stream, options.Limit));
        return stream;
    }

    public Task<List<Message>> GetMessagesAsync(
        string clusterId, string topic, int partition, FetchOptions options,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(FakeSyncKafkaClient.GenerateMessages(options.Limit));

    private static void FillStream(MessageStream stream, int count)
    {
        var messages = FakeSyncKafkaClient.GenerateMessages(count > 0 ? count : 50);
        stream.Messages.AddRange(messages);
        stream.HasMore = false;
    }

    private static List<Topic> GenerateTopics(string clusterId) =>
        Enumerable.Range(0, 15)
            .Select(i => new Topic($"bench-topic-{i:D3}", partitionCount: 3))
            .ToList();
}
