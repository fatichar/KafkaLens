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
    : IKafkaLensClient, IStreamingKafkaLensClient, IConnectionTestClient, ICancellableConnectionClient
{
    public string Name => "Shared";
    public bool CanEditClusters => false;
    public bool CanSaveMessages => true;

    // key = clusterInfo id, value = kafka clusterInfo
    private ReadOnlyDictionary<string, Shared.Entities.ClusterInfo> Clusters => infoRepository.GetAll();

    // key = clusterInfo id, value = kafka consumer
    private readonly ConcurrentDictionary<string, (string Address, IKafkaConsumer Consumer)> consumers = new();
    private readonly object consumerGate = new();

    #region Create
    public async Task<bool> ValidateConnectionAsync(string address)
        => (await ValidateConnectionWithDetailsAsync(address).ConfigureAwait(false)).Succeeded;

    public Task<ConnectionValidationResult> ValidateConnectionWithDetailsAsync(string address)
        => ValidateConnectionWithDetailsAsync(address, CancellationToken.None);

    public Task<ConnectionValidationResult> ValidateConnectionWithDetailsAsync(string address, CancellationToken cancellationToken)
    {
        return Task.Run(async () =>
        {
            IKafkaConsumer? consumer = null;
            var temporary = false;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                consumer = GetOrCreateConsumerByAddress(address, out temporary);
                return await consumer.ValidateConnectionWithDetailsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                return ConnectionValidationResult.Failed(e.Message, e.ToString());
            }
            finally
            {
                if (temporary && consumer != null)
                    DisposeConsumer(consumer);
            }
        }, cancellationToken);
    }

    private IKafkaConsumer GetOrCreateConsumerByAddress(string address, out bool temporary)
    {
        lock (consumerGate)
        {
            var cluster = Clusters.Values.FirstOrDefault(c => c.Address == address && c.IsEnabled)
                ?? Clusters.Values.FirstOrDefault(c => c.Address == address);
            temporary = cluster == null;
            if (cluster != null)
                return GetConsumer(cluster.Id);
        }
        // Address not associated with any known cluster, create a temporary consumer
        return consumerFactory.CreateNew(address);
    }

    public async Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        Validate(newCluster);

        var cluster = Shared.Entities.ClusterInfo.Create(newCluster);
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
            // CreateConsumer is called directly here; the outer GetOrAdd in GetConsumer
            // handles the ConcurrentDictionary insertion, so no nested GetOrAdd needed.
            return CreateConsumer(clusterInfo.Address);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to create consumer", clusterInfo);
            throw;
        }
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
        return Task.Run(async () =>
        {
            var result = new List<KafkaCluster>();
            foreach (var c in Clusters.Values.ToList())
            {
                var model = ToModel(c);
                if (c.IsEnabled && consumers.ContainsKey(c.Id))
                {
                    try
                    {
                        var validation = await GetConsumer(c.Id).ValidateConnectionWithDetailsAsync().ConfigureAwait(false);
                        model.Status = validation.Succeeded ? ConnectionState.Connected : ConnectionState.Failed;
                    }
                    catch (Exception e)
                    {
                        Log.Debug("ValidateConnection failed for cluster {ClusterName}: {Message}", c.Name, e.Message);
                        model.Status = ConnectionState.Failed;
                    }
                }
                result.Add(model);
            }
            return (IEnumerable<KafkaCluster>)result;
        });
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

    public IAsyncEnumerable<Message> StreamMessagesAsync(
        string clusterId,
        string topic,
        FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var consumer = GetStreamingConsumer(clusterId);
        return consumer.StreamMessagesAsync(topic, options, cancellationToken);
    }

    public IAsyncEnumerable<Message> StreamMessagesAsync(
        string clusterId,
        string topic,
        int partition,
        FetchOptions options,
        CancellationToken cancellationToken = default)
    {
        var consumer = GetStreamingConsumer(clusterId);
        return consumer.StreamMessagesAsync(topic, partition, options, cancellationToken);
    }
    #endregion Read

    #region update
    public async Task<KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update)
    {
        lock (consumerGate)
        {
            var existing = ValidateClusterId(clusterId);
            var addressChanged = existing.Address != update.Address;
            existing.Name = update.Name;
            existing.Address = update.Address;
            infoRepository.Update(existing);
            if (addressChanged && consumers.TryRemove(clusterId, out var oldConsumer))
                DisposeConsumer(oldConsumer.Consumer);
        }
        return await GetClusterByIdAsync(clusterId);
    }
    #endregion update

    #region Delete
    public async Task RemoveClusterByIdAsync(string clusterId)
    {
        lock (consumerGate)
        {
            infoRepository.Delete(clusterId);
            if (consumers.TryRemove(clusterId, out var oldConsumer))
                DisposeConsumer(oldConsumer.Consumer);
        }
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
        lock (consumerGate)
        {
            if (!Clusters.TryGetValue(clusterId, out var cluster))
                throw new ArgumentException("Unknown clusterInfo", nameof(clusterId));
            if (!cluster.IsEnabled)
                throw new InvalidOperationException("The cluster is disabled.");
            if (consumers.TryGetValue(clusterId, out var cached))
            {
                if (cached.Address == cluster.Address)
                    return cached.Consumer;
                consumers.TryRemove(clusterId, out _);
                DisposeConsumer(cached.Consumer);
            }
            var consumer = Connect(cluster);
            consumers[clusterId] = (cluster.Address, consumer);
            return consumer;
        }
    }

    private static void DisposeConsumer(IKafkaConsumer consumer)
    {
        try
        {
            consumer.Dispose();
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to dispose stale consumer");
        }
    }

    private IStreamingKafkaConsumer GetStreamingConsumer(string clusterId)
    {
        var consumer = GetConsumer(clusterId);
        if (consumer is IStreamingKafkaConsumer streamingConsumer)
        {
            return streamingConsumer;
        }

        throw new NotSupportedException("The configured Kafka consumer does not support bounded streaming.");
    }
    #endregion Validations

    #region Mappers
    private KafkaCluster ToModel(Shared.Entities.ClusterInfo clusterInfo)
    {
        return new KafkaCluster(clusterInfo.Id, clusterInfo.Name, clusterInfo.Address)
        {
            IsEnabled = clusterInfo.IsEnabled
        };
    }
    #endregion Mappers
}
