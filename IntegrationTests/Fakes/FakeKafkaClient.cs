using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bogus;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;

namespace IntegrationTests.Fakes;

public class FakeKafkaClient : IKafkaLensClient
{
    private readonly Faker _faker = new Faker();
    private readonly Dictionary<string, KafkaCluster> _clusters = new();
    private readonly Dictionary<string, List<Topic>> _topicsByCluster = new();

    public string Name => "Fake Client";
    public bool CanEditClusters => true;
    public bool CanSaveMessages => true;

    public FakeKafkaClient()
    {
    }

    public Task<bool> ValidateConnectionAsync(string bootstrapServers)
    {
        return Task.FromResult(true);
    }

    public Task<IEnumerable<KafkaCluster>> GetAllClustersAsync()
    {
        return Task.FromResult(_clusters.Values.AsEnumerable());
    }

    public Task<KafkaCluster> GetClusterByIdAsync(string id)
    {
        return Task.FromResult(_clusters[id]);
    }

    public Task<KafkaCluster> GetClusterByNameAsync(string name)
    {
        var cluster = _clusters.Values.FirstOrDefault(c => c.Name == name);
        if (cluster == null)
        {
            throw new Exception("Cluster not found");
        }
        return Task.FromResult(cluster);
    }

    public Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        var cluster = new KafkaCluster(Guid.NewGuid().ToString(), newCluster.Name, newCluster.Address);
        _clusters[cluster.Id] = cluster;
        _topicsByCluster[cluster.Id] = GenerateFakeTopics(cluster.Id);
        return Task.FromResult(cluster);
    }

    public Task<KafkaCluster> AddClusterAsync(KafkaCluster cluster)
    {
        _clusters[cluster.Id] = cluster;
        _topicsByCluster[cluster.Id] = GenerateFakeTopics(cluster.Id);
        return Task.FromResult(cluster);
    }

    public Task UpdateClusterAsync(KafkaCluster cluster)
    {
        _clusters[cluster.Id] = cluster;
        return Task.CompletedTask;
    }

    public Task<KafkaCluster> UpdateClusterAsync(string id, KafkaClusterUpdate update)
    {
        if (_clusters.TryGetValue(id, out var cluster))
        {
            var updated = new KafkaCluster(cluster.Id, update.Name, update.Address);
            _clusters[id] = updated;
            return Task.FromResult(updated);
        }
        throw new Exception("Cluster not found");
    }

    public Task RemoveClusterByIdAsync(string id)
    {
        _clusters.Remove(id);
        _topicsByCluster.Remove(id);
        return Task.CompletedTask;
    }

    public Task DeleteClusterAsync(string id)
    {
        _clusters.Remove(id);
        _topicsByCluster.Remove(id);
        return Task.CompletedTask;
    }

    public Task<IList<Topic>> GetTopicsAsync(string clusterId)
    {
        if (_topicsByCluster.TryGetValue(clusterId, out var topics))
        {
            return Task.FromResult<IList<Topic>>(topics);
        }
        return Task.FromResult<IList<Topic>>(new List<Topic>());
    }

    public MessageStream GetMessageStream(string clusterId, string topic, FetchOptions options, CancellationToken cancellationToken = default)
    {
        var stream = new MessageStream();
        LoadFakeMessages(stream, clusterId, topic, options.Limit);
        return stream;
    }

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, FetchOptions options, CancellationToken cancellationToken = default)
    {
        var messages = GenerateFakeMessages(clusterId, topic, options.Limit);
        return Task.FromResult(messages);
    }

    public MessageStream GetMessageStream(string clusterId, string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default)
    {
        var stream = new MessageStream();
        LoadFakeMessages(stream, clusterId, topic, options.Limit);
        return stream;
    }

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default)
    {
        var messages = GenerateFakeMessages(clusterId, topic, options.Limit);
        return Task.FromResult(messages);
    }

    private void LoadFakeMessages(MessageStream stream, string clusterId, string topic, int count)
    {
        var msgs = GenerateFakeMessages(clusterId, topic, count);
        stream.Messages.AddRange(msgs);
        stream.HasMore = false;
    }

    private List<Topic> GenerateFakeTopics(string clusterId)
    {
        var topicFaker = new Faker<Topic>()
            .CustomInstantiator(f => new Topic(
                f.Commerce.Department() + "_" + f.Random.Word(),
                f.Random.Int(1, 5)
            ));

        return topicFaker.Generate(_faker.Random.Int(3, 8));
    }

    private List<Message> GenerateFakeMessages(string clusterId, string topic, int count)
    {
        var countToGen = count > 0 ? count : 10;
        var messageFaker = new Faker<Message>()
            .CustomInstantiator(f => {
                var msg = new Message(
                    f.Date.PastOffset().ToUnixTimeMilliseconds(),
                    new Dictionary<string, byte[]>(),
                    f.Random.Bytes(10),
                    f.Random.Bytes(100)
                );
                msg.Partition = f.Random.Int(0, 3);
                msg.Offset = f.Random.Long(100, 10000);
                return msg;
            });

        return messageFaker.Generate(countToGen);
    }
}
