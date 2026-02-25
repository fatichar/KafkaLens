using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using KafkaLens.Core.Utils;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using Serilog;
using KafkaCluster = KafkaLens.Shared.Models.KafkaCluster;

namespace KafkaLens.Core.Services;

public class SharedClient(
    IClusterInfoRepository infoRepository,
    ConsumerFactory consumerFactory)
    : IKafkaLensClient
{
    public string Name => "Shared";
    public bool CanEditClusters => false;
    public bool CanSaveMessages => true;

    // key = clusterInfo id, value = kafka clusterInfo
    private ReadOnlyDictionary<string, Shared.Entities.ClusterInfo> Clusters => infoRepository.GetAll();

    // key = clusterInfo id, value = kafka consumer
    private readonly ConcurrentDictionary<string, IKafkaConsumer> consumers = new();

    #region Create
    public Task<bool> ValidateConnectionAsync(string address)
    {
        return Task.Run(() =>
        {
            try
            {
                var consumer = GetOrCreateConsumerByAddress(address);
                return consumer.ValidateConnection();
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    private IKafkaConsumer GetOrCreateConsumerByAddress(string address)
    {
        var cluster = Clusters.Values.FirstOrDefault(c => c.Address == address);
        if (cluster != null)
        {
            return consumers.GetOrAdd(cluster.Id, _ => CreateConsumer(cluster.Address));
        }
        // Address not associated with any known cluster, create a temporary consumer
        return consumerFactory.CreateNew(address);
    }

    public async Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        Validate(newCluster);

        var cluster = CreateCluster(newCluster);
        try
        {
            infoRepository.Add(cluster);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to save clusterInfo");
            throw;
        }

        return ToModel(cluster);
    }

    private IKafkaConsumer Connect(Shared.Entities.ClusterInfo clusterInfo)
    {
        try
        {
            return consumers.GetOrAdd(clusterInfo.Id, _ => CreateConsumer(clusterInfo.Address));
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to create consumer", clusterInfo);
            throw;
        }
    }

    private static Shared.Entities.ClusterInfo CreateCluster(NewKafkaCluster newCluster)
    {
        return new Shared.Entities.ClusterInfo(
            Guid.NewGuid().ToString(),
            newCluster.Name,
            newCluster.Address);
    }

    private IKafkaConsumer CreateConsumer(string address)
    {
        return consumerFactory.CreateNew(address);
    }
    #endregion Create

    #region Read
    public Task<IEnumerable<KafkaCluster>> GetAllClustersAsync()
    {
        Log.Information("Get all clusters");
        return Task.Run(() => Clusters.Values.Select(c =>
        {
            var model = ToModel(c);
            if (consumers.TryGetValue(c.Id, out var consumer))
            {
                try
                {
                    model.IsConnected = consumer.ValidateConnection();
                }
                catch (Exception e)
                {
                    Log.Debug("ValidateConnection failed for cluster {ClusterName}: {Message}", c.Name, e.Message);
                    model.IsConnected = false;
                }
            }
            return model;
        }).AsEnumerable());
    }

    public Task<KafkaCluster> GetClusterByIdAsync(string clusterId)
    {
        var cluster = ValidateClusterId(clusterId);
        return Task.Run(() => ToModel(cluster));
    }

    Task<KafkaCluster> IKafkaLensClient.GetClusterByNameAsync(string name)
    {
        var cluster = ValidateClusterId(name);
        return Task.FromResult(ToModel(cluster));
    }

    public Task<IList<Topic>> GetTopicsAsync(string clusterId)
    {
        var consumer = GetConsumer(clusterId);

        return Task.Run(() =>
        {
            var topics = consumer.GetTopics();
            topics.Sort(Helper.CompareTopics);
            return (IList<Topic>) topics;
        });
    }

    public MessageStream GetMessageStream(
        string clusterId,
        string topic,
        FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var consumer = GetConsumer(clusterId);
        return consumer.GetMessageStream(topic, options, cancellationToken);
    }

    public async Task<List<Message>> GetMessagesAsync(
        string clusterId,
        string topic,
        FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var consumer = GetConsumer(clusterId);
        return await consumer.GetMessagesAsync(topic, options, cancellationToken);
    }

    public MessageStream GetMessageStream(
        string clusterId,
        string topic,
        int partition,
        FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var consumer = GetConsumer(clusterId);
        return consumer.GetMessageStream(topic, partition, options, cancellationToken);
    }

    public async Task<List<Message>> GetMessagesAsync(
        string clusterId,
        string topic,
        int partition,
        FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var consumer = GetConsumer(clusterId);
        return await consumer.GetMessagesAsync(topic, partition, options, cancellationToken);
    }
    #endregion Read

    #region update
    public async Task<KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update)
    {
        var existing = ValidateClusterId(clusterId);
        existing.Name = update.Name;
        existing.Address = update.Address;
        infoRepository.Update(existing);
        return await GetClusterByIdAsync(clusterId);
    }
    #endregion update

    #region Delete
    public async Task RemoveClusterByIdAsync(string clusterId)
    {
        infoRepository.Delete(clusterId);
    }
    #endregion

    #region Validations
    private void Validate(NewKafkaCluster newCluster)
    {
        var all = Clusters.ToList();

        var existing = Clusters.Values.FirstOrDefault(cluster =>
            cluster.Name.Equals(newCluster.Name, StringComparison.InvariantCultureIgnoreCase));

        if (existing != null)
        {
            throw new ArgumentException($"Cluster with name {existing.Name} already exists");
        }
    }

    private Shared.Entities.ClusterInfo ValidateClusterId(string id)
    {
        Clusters.TryGetValue(id, out var cluster);
        if (cluster == null)
        {
            throw new ArgumentException("", nameof(id));
        }
        return cluster;
    }

    private IKafkaConsumer GetConsumer(string clusterId)
    {
        if (Clusters.TryGetValue(clusterId, out var cluster))
        {
            return consumers.GetOrAdd(clusterId, _ => Connect(cluster));
        }
        throw new ArgumentException("Unknown clusterInfo", nameof(clusterId));
    }
    #endregion Validations

    #region Mappers
    private KafkaCluster ToModel(Shared.Entities.ClusterInfo clusterInfo)
    {
        return new KafkaCluster(clusterInfo.Id, clusterInfo.Name, clusterInfo.Address);
    }
    #endregion Mappers
}