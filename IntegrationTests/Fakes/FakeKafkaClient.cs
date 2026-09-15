using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;

namespace IntegrationTests.Fakes;

public class FakeKafkaClient : IKafkaLensClient
{
    private readonly IClusterInfoRepository _infoRepository;
    private readonly Dictionary<string, List<Topic>> _topicsByCluster = new();

    // Independently controllable failure modes, so tests can simulate scenarios like
    // "connectivity check succeeds but a subsequent topic/message fetch still fails"
    // (e.g. a stale cached validation vs. a real per-request broker error).
    private readonly HashSet<string> _unreachableAddresses = new(StringComparer.Ordinal);
    private readonly HashSet<string> _topicsFailureClusterIds = new(StringComparer.Ordinal);
    private readonly HashSet<string> _messagesFailureClusterIds = new(StringComparer.Ordinal);

    public Dictionary<string, int> ValidationCalls { get; } = new(StringComparer.Ordinal);
    public int DiscoveryCalls { get; private set; }
    public int TopicsCalls { get; private set; }
    public int MessagesCalls { get; private set; }

    public void ResetCalls()
    {
        ValidationCalls.Clear();
        DiscoveryCalls = 0;
        TopicsCalls = 0;
        MessagesCalls = 0;
    }

    public string Name => "Local";
    public bool CanEditClusters => true;
    public bool CanSaveMessages => true;

    public FakeKafkaClient(IClusterInfoRepository infoRepository)
    {
        _infoRepository = infoRepository;
    }

    public void Reset()
    {
        _topicsByCluster.Clear();
        _unreachableAddresses.Clear();
        _topicsFailureClusterIds.Clear();
        _messagesFailureClusterIds.Clear();
        ResetCalls();
    }

    /// <summary>Simulates VPN/broker reachability for a given address. Reachable by default.</summary>
    public void SetReachable(string address, bool reachable)
    {
        if (reachable) _unreachableAddresses.Remove(address);
        else _unreachableAddresses.Add(address);
    }

    /// <summary>Makes the cluster's topic-list fetch throw, independent of address reachability.</summary>
    public void SetTopicsFailure(string clusterId, bool shouldFail)
    {
        if (shouldFail) _topicsFailureClusterIds.Add(clusterId);
        else _topicsFailureClusterIds.Remove(clusterId);
    }

    /// <summary>
    /// Makes only message-level fetches for a cluster throw, while its topic-list fetch keeps
    /// succeeding. Models a broker/topic-metadata connection that looks healthy but can't
    /// actually serve message reads for a given topic/partition.
    /// </summary>
    public void SetMessagesFailure(string clusterId, bool shouldFail)
    {
        if (shouldFail) _messagesFailureClusterIds.Add(clusterId);
        else _messagesFailureClusterIds.Remove(clusterId);
    }

    public Task<bool> ValidateConnectionAsync(string bootstrapServers)
    {
        ValidationCalls[bootstrapServers] = ValidationCalls.GetValueOrDefault(bootstrapServers) + 1;
        return Task.FromResult(!_unreachableAddresses.Contains(bootstrapServers));
    }

    public Task<IEnumerable<KafkaCluster>> GetAllClustersAsync()
    {
        DiscoveryCalls++;
        return Task.FromResult<IEnumerable<KafkaCluster>>(_infoRepository.GetAll().Values.Select(ToModel).ToList());
    }

    public Task<KafkaCluster> GetClusterByIdAsync(string id)
    {
        return Task.FromResult(ToModel(_infoRepository.GetById(id)));
    }

    public Task<KafkaCluster> GetClusterByNameAsync(string name)
    {
        var cluster = _infoRepository.GetAll().Values.FirstOrDefault(c => c.Name == name);
        if (cluster == null)
        {
            throw new Exception("Cluster not found");
        }
        return Task.FromResult(ToModel(cluster));
    }

    public Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        var clusterInfo = _infoRepository.Add(newCluster.Name, newCluster.Address);
        _topicsByCluster[clusterInfo.Id] = GenerateFakeTopics(clusterInfo.Id);
        return Task.FromResult(ToModel(clusterInfo));
    }

    public Task<KafkaCluster> AddClusterAsync(KafkaCluster cluster)
    {
        var clusterInfo = new KafkaLens.Shared.Entities.ClusterInfo(cluster.Id, cluster.Name, cluster.Address) { IsEnabled = cluster.IsEnabled };
        _infoRepository.Add(clusterInfo);
        _topicsByCluster[cluster.Id] = GenerateFakeTopics(cluster.Id);
        return Task.FromResult(ToModel(clusterInfo));
    }

    public Task UpdateClusterAsync(KafkaCluster cluster)
    {
        var clusterInfo = new KafkaLens.Shared.Entities.ClusterInfo(cluster.Id, cluster.Name, cluster.Address) { IsEnabled = cluster.IsEnabled };
        _infoRepository.Update(clusterInfo);
        return Task.CompletedTask;
    }

    public Task<KafkaCluster> UpdateClusterAsync(string id, KafkaClusterUpdate update)
    {
        var clusterInfo = _infoRepository.GetById(id);
        clusterInfo.Name = update.Name;
        clusterInfo.Address = update.Address;
        _infoRepository.Update(clusterInfo);
        return Task.FromResult(ToModel(clusterInfo));
    }

    public Task RemoveClusterByIdAsync(string id)
    {
        _infoRepository.Delete(id);
        _topicsByCluster.Remove(id);
        return Task.CompletedTask;
    }

    public Task DeleteClusterAsync(string id)
    {
        _infoRepository.Delete(id);
        _topicsByCluster.Remove(id);
        return Task.CompletedTask;
    }

    private KafkaCluster ToModel(KafkaLens.Shared.Entities.ClusterInfo clusterInfo)
    {
        return new KafkaCluster(clusterInfo.Id, clusterInfo.Name, clusterInfo.Address) { IsEnabled = clusterInfo.IsEnabled };
    }

    private bool IsUnreachable(string clusterId) =>
        _unreachableAddresses.Contains(_infoRepository.GetById(clusterId).Address);

    public Task<IList<Topic>> GetTopicsAsync(string clusterId)
    {
        TopicsCalls++;
        if (IsUnreachable(clusterId) || _topicsFailureClusterIds.Contains(clusterId))
        {
            throw new InvalidOperationException($"Simulated topic fetch failure for cluster {clusterId}");
        }

        if (_topicsByCluster.TryGetValue(clusterId, out var topics))
        {
            return Task.FromResult<IList<Topic>>(topics);
        }
        return Task.FromResult<IList<Topic>>(new List<Topic>());
    }

    public MessageStream GetMessageStream(string clusterId, string topic, FetchOptions options, CancellationToken cancellationToken = default)
    {
        var stream = new MessageStream();
        Dispatcher.UIThread.Post(() => LoadFakeMessages(stream, clusterId, topic, null, options.Limit, cancellationToken));
        return stream;
    }

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, FetchOptions options, CancellationToken cancellationToken = default)
    {
        MessagesCalls++;
        if (IsUnreachable(clusterId) || _messagesFailureClusterIds.Contains(clusterId))
        {
            throw new InvalidOperationException($"Simulated message fetch failure for cluster {clusterId}");
        }

        var messages = GenerateFakeMessages(clusterId, topic, null, options.Limit);
        return Task.FromResult(messages);
    }

    public MessageStream GetMessageStream(string clusterId, string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default)
    {
        var stream = new MessageStream();
        Dispatcher.UIThread.Post(() => LoadFakeMessages(stream, clusterId, topic, partition, options.Limit, cancellationToken));
        return stream;
    }

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default)
    {
        MessagesCalls++;
        if (IsUnreachable(clusterId) || _messagesFailureClusterIds.Contains(clusterId))
        {
            throw new InvalidOperationException($"Simulated message fetch failure for cluster {clusterId}");
        }

        var messages = GenerateFakeMessages(clusterId, topic, partition, options.Limit);
        return Task.FromResult(messages);
    }

    private void LoadFakeMessages(MessageStream stream, string clusterId, string topic, int? partition, int count, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            stream.HasMore = false;
            return;
        }

        MessagesCalls++;
        if (IsUnreachable(clusterId) || _messagesFailureClusterIds.Contains(clusterId))
        {
            stream.SetError(new InvalidOperationException($"Simulated message fetch failure for cluster {clusterId}"));
            stream.HasMore = false;
            return;
        }

        var msgs = GenerateFakeMessages(clusterId, topic, partition, count);
        stream.Messages.AddRange(msgs);
        stream.HasMore = false;
    }

    private List<Topic> GenerateFakeTopics(string clusterId)
    {
        var clusterPrefix = clusterId.Length >= 8 ? clusterId[..8] : clusterId;
        return
        [
            new Topic($"orders_{clusterPrefix}", 3),
            new Topic($"payments_{clusterPrefix}", 2),
            new Topic($"shipments_{clusterPrefix}", 4)
        ];
    }

    private List<Message> GenerateFakeMessages(string clusterId, string topic, int? partition, int count)
    {
        var countToGen = count > 0 ? count : 10;
        var messages = new List<Message>(countToGen);

        for (var i = 0; i < countToGen; i++)
        {
            var selectedPartition = partition ?? i % 3;
            var msg = new Message(
                1_700_000_000_000 + i,
                new Dictionary<string, byte[]>(),
                System.Text.Encoding.UTF8.GetBytes($"key-{clusterId}-{topic}-{selectedPartition}-{i}"),
                System.Text.Encoding.UTF8.GetBytes($"value-{clusterId}-{topic}-{selectedPartition}-{i}")
            );
            msg.Partition = selectedPartition;
            msg.Offset = i;
            messages.Add(msg);
        }

        return messages;
    }
}
