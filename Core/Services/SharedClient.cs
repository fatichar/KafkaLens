using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using KafkaLens.Core.Utils;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using Serilog;
using KafkaCluster = KafkaLens.Shared.Models.KafkaCluster;

namespace KafkaLens.Core.Services;

public class SharedClient : IKafkaLensClient
{
    public string Name => "Shared";
    public bool CanEditClusters => false;
    public bool CanSaveMessages => true;
    private readonly IClusterInfoRepository infoRepository;
    private readonly ConsumerFactory consumerFactory;

    // key = clusterInfo id, value = kafka clusterInfo
    private ReadOnlyDictionary<string, Shared.Entities.ClusterInfo> Clusters => infoRepository.GetAll();

    // key = clusterInfo id, value = kafka consumer
    private readonly IDictionary<string, IKafkaConsumer> consumers = new Dictionary<string, IKafkaConsumer>();

    public SharedClient(
        IClusterInfoRepository infoRepository,
        ConsumerFactory consumerFactory)
    {
        this.infoRepository = infoRepository;
        this.consumerFactory = consumerFactory;
    }

    #region Create
    public Task<bool> ValidateConnectionAsync(string address)
    {
        return Task.FromResult(false);
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
        return Task.FromResult(Clusters.Values.Select(ToModel));
    }

    public Task<KafkaCluster> GetClusterByIdAsync(string clusterId)
    {
        var cluster = ValidateClusterId(clusterId);
        return Task.FromResult(ToModel(cluster));
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
        if (consumers.TryGetValue(clusterId, out var consumer))
        {
            return consumer;
        }
        if (Clusters.TryGetValue(clusterId, out var cluster))
        {
            return Connect(cluster);
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