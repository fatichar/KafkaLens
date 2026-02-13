using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KafkaLens.Clients;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using KafkaCluster = KafkaLens.Shared.Models.KafkaCluster;
using Message = KafkaLens.Shared.Models.Message;
using NewKafkaCluster = KafkaLens.Shared.Models.NewKafkaCluster;
using Topic = KafkaLens.Shared.Models.Topic;

namespace KafkaLens.RestClient;

public class RestClient : IKafkaLensClient
{
    public string Name { get; } = "Rest";
    public bool CanEditClusters => false;
    private readonly KLRestClient client;
    public Task<bool> ValidateConnectionAsync(string bootstrapServers)
    {
        throw new NotImplementedException();
    }

    public Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        throw new NotImplementedException();
    }

    public async Task<IEnumerable<KafkaCluster>> GetAllClusters()
    {
        return await client.GetAllClustersAsync();
    }

    public KafkaCluster GetClusterById(string clusterId)
    {
        throw new NotImplementedException();
    }

    public KafkaCluster GetClusterByName(string name)
    {
        throw new NotImplementedException();
    }

    public IList<Topic> GetTopics(string clusterId)
    {
        throw new NotImplementedException();
    }

    public MessageStream GetMessageStream(string clusterId, string topic, FetchOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, FetchOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public MessageStream GetMessageStream(string clusterId, string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<List<Message>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public KafkaCluster UpdateCluster(string clusterId, KafkaClusterUpdate update)
    {
        throw new NotImplementedException();
    }

    public Task<KafkaCluster> RemoveClusterByIdAsync(string clusterId)
    {
        throw new NotImplementedException();
    }
}