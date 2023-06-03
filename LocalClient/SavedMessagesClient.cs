using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using KafkaLens.Clients;
using KafkaLens.Core.Services;
using KafkaLens.Core.Utils;
using KafkaLens.Shared;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace KafkaLens;

public class SavedMessagesClient : ISavedMessagesClient
{
    private readonly ILogger<LocalClient> logger;
    private readonly IServiceScopeFactory scopeFactory;

    // key = clusterInfo id, value = kafka clusterInfo
    private readonly Dictionary<string, ClusterInfo> clusters = new();

    // key = clusterInfo id, value = kafka consumer
    private readonly IDictionary<string, IKafkaConsumer> consumers = new Dictionary<string, IKafkaConsumer>();

    public SavedMessagesClient(
        [NotNull] ILogger<LocalClient> logger,
        [NotNull] IServiceScopeFactory scopeFactory)
    {
        this.logger = logger;
        this.scopeFactory = scopeFactory;
    }

    #region Create

    public string Name { get; } = "Saved Messages";

    public Task<bool> ValidateConnectionAsync(string BootstrapServers)
    {
        return Task.FromResult(false);
    }

    public async Task<Shared.Models.KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        Validate(newCluster);

        var cluster = CreateCluster(newCluster);
        clusters.Add(cluster.Id, cluster);

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
            logger.LogError(e, "Failed to create consumer", clusterInfo);
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

    private IKafkaConsumer CreateConsumer(string bootstrapServers)
    {
        return new SavedMessagesConsumer(bootstrapServers);
    }
    #endregion Create

    #region Read
    public Task<IEnumerable<Shared.Models.KafkaCluster>> GetAllClustersAsync()
    {
        Log.Information("GetById all clusters");
        return Task.FromResult(clusters.Values.Select(ToModel));
    }

    public Task<Shared.Models.KafkaCluster> GetClusterByIdAsync(string clusterId)
    {
        var cluster = ValidateClusterId(clusterId);
        return Task.FromResult(ToModel(cluster));
    }

    Task<Shared.Models.KafkaCluster> IKafkaLensClient.GetClusterByNameAsync(string name)
    {
        var cluster = ValidateClusterId(name);
        return Task.FromResult(ToModel(cluster));
    }

    public Task<IList<Topic>> GetTopicsAsync([DisallowNull] string clusterId)
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
        FetchOptions options)
    {
        var consumer = GetConsumer(clusterId);
        return consumer.GetMessageStream(topic, options);
    }

    public async Task<List<Message>> GetMessagesAsync(
        string clusterId,
        string topic,
        FetchOptions options)
    {
        var consumer = GetConsumer(clusterId);
        return await consumer.GetMessagesAsync(topic, options);
    }

    public MessageStream GetMessageStream(
        string clusterId,
        string topic,
        int partition,
        FetchOptions options)
    {
        var consumer = GetConsumer(clusterId);
        return consumer.GetMessageStream(topic, partition, options);
    }

    public async Task<List<Message>> GetMessagesAsync(
        string clusterId,
        string topic,
        int partition,
        FetchOptions options)
    {
        var consumer = GetConsumer(clusterId);
        return await consumer.GetMessagesAsync(topic, partition, options);
    }
    #endregion Read

    #region update
    public async Task<Shared.Models.KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update)
    {
        return null;
    }
    #endregion update

    #region Delete
    public async Task RemoveClusterByIdAsync(string clusterId)
    {
        if (!clusters.ContainsKey(clusterId))
        {
            throw new KeyNotFoundException($"Cluster with id {clusterId} not found");
        }
        clusters.Remove(clusterId);
    }
    #endregion

    #region Validations
    private void Validate(NewKafkaCluster newCluster)
    {
        var all = clusters.ToList();

        var existing = clusters.Values.FirstOrDefault(cluster =>
            cluster.Name.Equals(newCluster.Name, StringComparison.InvariantCultureIgnoreCase));

        if (existing != null)
        {
            throw new ArgumentException($"Cluster with name {existing.Name} already exists");
        }
    }

    private ClusterInfo ValidateClusterId(string id)
    {
        clusters.TryGetValue(id, out var cluster);
        if (cluster == null)
        {
            throw new ArgumentException("", nameof(id));
        }
        return cluster;
    }

    private ClusterInfo validateClusterName(string name)
    {
        var cluster = clusters.Values
            .FirstOrDefault(cluster => cluster.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase));
        if (cluster == null)
        {
            throw new ArgumentException($"Cluster with name {name} does not exist", nameof(name));
        }
        return cluster;
    }

    [return: NotNull]
    private IKafkaConsumer GetConsumer(string clusterId)
    {
        if (consumers.TryGetValue(clusterId, out var consumer))
        {
            return consumer;
        }
        if (clusters.TryGetValue(clusterId, out var cluster))
        {
            return Connect(cluster);
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