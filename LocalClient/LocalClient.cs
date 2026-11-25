using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using KafkaLens.Core.Services;
using KafkaLens.Core.Utils;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Security;
using Serilog;

namespace KafkaLens.Clients;

public class LocalClient : IKafkaLensClient, IConnectionTestClient, ICancellableConnectionClient, ISecureConnectionTestClient
{
    public string Name { get; } = "Local";
    public bool CanEditClusters => true;
    public bool CanSaveMessages => true;

    private readonly IClusterInfoRepository infoRepository;
    private readonly ConsumerFactory consumerFactory;

    public LocalClient(IClusterInfoRepository infoRepository, KafkaConfig kafkaConfig)
        : this(infoRepository, kafkaConfig, null)
    {
    }

    internal LocalClient(IClusterInfoRepository infoRepository, KafkaConfig kafkaConfig, ConsumerFactory? factory)
    {
        this.infoRepository = infoRepository;
        this.consumerFactory = factory ?? new(kafkaConfig);
    }

    // key = clusterInfo id, value = kafka clusterInfo
    private ReadOnlyDictionary<string, ClusterInfo> Clusters => infoRepository.GetAll();

    // key = clusterInfo id, value = kafka consumer
    private readonly ConcurrentDictionary<string, (string Fingerprint, IKafkaConsumer Consumer)> consumers = new();
    private readonly object consumerGate = new();

    #region Create
    public async Task<bool> ValidateConnectionAsync(string address)
    {
        var result = await ValidateConnectionWithDetailsAsync(address);
        return result.Succeeded;
    }

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
                Log.Error(e, "Connection validation failed for {Address}", address);
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

    public async Task<Shared.Models.KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        Validate(newCluster);

        var cluster = ClusterInfo.Create(newCluster);
        ApplySecurity(cluster, newCluster);
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
            return consumerFactory.CreateFor(KafkaConnectionSettings.From(clusterInfo));
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
    public Task<ConnectionValidationResult> ValidateConnectionWithDetailsAsync(KafkaConnectionSettings settings, CancellationToken cancellationToken)
    {
        // Never resolve a saved cluster by address here: the explicit settings (e.g. switching a saved
        // TLS cluster to plaintext) must be tested as entered, with a temporary consumer.
        return Task.Run(async () =>
        {
            IKafkaConsumer? consumer = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Always a temporary consumer: the settings may not be saved yet.
                consumer = consumerFactory.CreateFor(settings);
                return await consumer.ValidateConnectionWithDetailsAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                var secrets = settings.Secrets.Values;
                Log.Warning("Connection validation failed for {Connection}: {Error}", settings, SecretRedactor.Redact(e.Message, secrets));
                return ConnectionValidationResult.Failed(SecretRedactor.Redact(e.Message, secrets), SecretRedactor.Redact(e.ToString(), secrets));
            }
            finally
            {
                if (consumer != null) DisposeConsumer(consumer);
            }
        }, cancellationToken);
    }
    #endregion Create

    #region Read
    public Task<IEnumerable<Shared.Models.KafkaCluster>> GetAllClustersAsync()
    {
        Log.Information("Get all Clusters");
        return Task.Run<IEnumerable<Shared.Models.KafkaCluster>>(() => Clusters.Values.Select(c =>
        {
            var model = ToModel(c);
            model.Status = ConnectionState.Unknown; // Initial state, will be updated by background check
            return model;
        }).ToList());
    }

    public Task<Shared.Models.KafkaCluster> GetClusterByIdAsync(string clusterId)
    {
        return Task.Run(() =>
        {
            var cluster = ValidateClusterId(clusterId);
            return ToModel(cluster);
        });
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
        lock (consumerGate)
        {
            var existing = ValidateClusterId(clusterId);
            var oldFingerprint = KafkaConnectionSettings.From(existing).Fingerprint();
            existing.Name = update.Name;
            existing.Address = update.Address;
            if (update.Security != null)
                existing.Security = update.Security.Normalized() is { IsDefault: false } security ? security : null;
            if (update.Security != null || update.Secrets != null)
                existing.Secrets = ClusterSecurity.MergeSecrets(existing.Secrets, update.Secrets, existing.Security ?? new ClusterSecurity());
            infoRepository.Update(existing);
            var addressChanged = oldFingerprint != KafkaConnectionSettings.From(existing).Fingerprint();

        // The cached consumer (if any) was created against the old address; drop it so the
        // next access reconnects using the updated address instead of reusing a stale one.
            if (addressChanged && consumers.TryRemove(clusterId, out var oldConsumer))
                DisposeConsumer(oldConsumer.Consumer);
        }

        return await GetClusterByIdAsync(clusterId);
    }

    private static void DisposeConsumer(IKafkaConsumer consumer)
    {
        try
        {
            consumer.Dispose();
        }
        catch (Exception e)
        {
            Log.Warning(e, "Failed to dispose stale consumer after cluster update");
        }
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

    private ClusterInfo ValidateClusterId(string id)
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
                if (cached.Fingerprint == KafkaConnectionSettings.From(cluster).Fingerprint())
                    return cached.Consumer;
                consumers.TryRemove(clusterId, out _);
                DisposeConsumer(cached.Consumer);
            }
            var consumer = Connect(cluster);
            consumers[clusterId] = (KafkaConnectionSettings.From(cluster).Fingerprint(), consumer);
            return consumer;
        }
    }
    #endregion Validations

    #region Mappers
    private Shared.Models.KafkaCluster ToModel(ClusterInfo clusterInfo)
    {
        return new Shared.Models.KafkaCluster(clusterInfo.Id, clusterInfo.Name, clusterInfo.Address)
        {
            IsEnabled = clusterInfo.IsEnabled,
            Security = clusterInfo.Security?.Clone() ?? new ClusterSecurity(),
            StoredSecretNames = clusterInfo.Secrets?.Keys.ToList() ?? []
        };
    }

    private static void ApplySecurity(Shared.Entities.ClusterInfo cluster, NewKafkaCluster newCluster)
    {
        var security = newCluster.Security?.Normalized();
        cluster.Security = security is { IsDefault: false } ? security : null;
        cluster.Secrets = ClusterSecurity.MergeSecrets(null, newCluster.Secrets, cluster.Security ?? new ClusterSecurity());
    }
    #endregion Mappers
}