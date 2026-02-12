using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using KafkaLens.Core.Services;
using KafkaLens.Core.Utils;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using Serilog;

namespace KafkaLens.Clients;

public class LocalClient : IKafkaLensClient
{
    public string Name { get; } = "Local";
    public bool CanEditClusters => true;
    public bool CanSaveMessages => true;

    private readonly IClusterInfoRepository infoRepository;
    private readonly ConsumerFactory consumerFactory;

    // key = clusterInfo id, value = kafka clusterInfo
    private ReadOnlyDictionary<string, ClusterInfo> Clusters => infoRepository.GetAll();

    // key = clusterInfo id, value = kafka consumer
    private readonly IDictionary<string, IKafkaConsumer> consumers = new Dictionary<string, IKafkaConsumer>();

    public LocalClient(
        IClusterInfoRepository infoRepository)
    {
        this.infoRepository = infoRepository;
        consumerFactory = new ConsumerFactory();
    }

    #region Create
    public Task<bool> ValidateConnectionAsync(string address)
    {
        return Task.Run(() =>
        {
            try
            {
                using var consumer = consumerFactory.CreateNew(address);
                return consumer.ValidateConnection();
            }
            catch (Exception)
            {
                return false;
            }
        });
    }

    public async Task<Shared.Models.KafkaCluster> AddAsync(NewKafkaCluster newCluster)
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

    private IKafkaConsumer Connect(ClusterInfo clusterInfo)
    {
        try
        {
            var consumer = CreateConsumer(clusterInfo.Address);
            consumers.Add(clusterInfo.Id, consumer);
            return consumer;
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to create consumer", clusterInfo);
            throw;
        }
    }

    private static ClusterInfo CreateCluster(NewKafkaCluster newCluster)
    {
        return new ClusterInfo(
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
    public Task<IEnumerable<Shared.Models.KafkaCluster>> GetAllClustersAsync()
    {
        Log.Information("Get all Clusters");
        return Task.FromResult(Clusters.Values.Select(ToModel));
    }

    public Task<Shared.Models.KafkaCluster> GetClusterByIdAsync(string clusterId)
    {
        var cluster = ValidateClusterId(clusterId);
        return Task.FromResult(ToModel(cluster));
    }

    public async Task<Shared.Models.KafkaCluster> GetClusterByNameAsync(string name)
    {
        var cluster = ValidateClusterId(name);
        return ToModel(cluster);
    }

    public async Task<IList<Topic>> GetTopicsAsync(string clusterId)
    {
        var consumer = GetConsumer(clusterId);

        return await Task.Run(() =>
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
    public async Task<Shared.Models.KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update)
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

    private ClusterInfo ValidateClusterId(string id)
    {
        Clusters.TryGetValue(id, out var cluster);
        if (cluster == null)
        {
            throw new ArgumentException("", nameof(id));
        }
        return cluster;
    }

    private ClusterInfo validateClusterName(string name)
    {
        var cluster = Clusters.Values
            .FirstOrDefault(cluster => cluster.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        if (cluster == null)
        {
            throw new ArgumentException($"Cluster with name {name} does not exist", nameof(name));
        }
        return cluster;
    }

    private IKafkaConsumer GetConsumer(string clusterId)
    {
        lock (consumers)
        {
            if (consumers.TryGetValue(clusterId, out var consumer))
            {
                return consumer;
            }
            if (Clusters.TryGetValue(clusterId, out var cluster))
            {
                return Connect(cluster);
            }
        }
        throw new ArgumentException("Unknown clusterInfo", nameof(clusterId));
    }
    #endregion Validations

    #region Mappers
    private Shared.Models.KafkaCluster ToModel(ClusterInfo clusterInfo)
    {
        return new Shared.Models.KafkaCluster(clusterInfo.Id, clusterInfo.Name, clusterInfo.Address);
    }
    #endregion Mappers
}