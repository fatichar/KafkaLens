using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using KafkaLens.ViewModels.DataAccess;
using KafkaLens.Core.Services;
using KafkaLens.Core.Utils;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Entities = KafkaLens.ViewModels.Entities;

namespace KafkaLens.UI;

public class LocalClient : IKafkaLensClient
{
    private readonly ILogger<LocalClient> logger;
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ConsumerFactory consumerFactory;

    // key = cluster id, value = kafka cluster
    private readonly Dictionary<string, Entities.KafkaCluster> clusters;

    // key = cluster id, value = kafka consumer
    private readonly IDictionary<string, IKafkaConsumer> consumers = new Dictionary<string, IKafkaConsumer>();

    public LocalClient(
        [NotNull] ILogger<LocalClient> logger,
        [NotNull] IServiceScopeFactory scopeFactory,
        [NotNull] ConsumerFactory consumerFactory)
    {
        this.logger = logger;
        this.scopeFactory = scopeFactory;
        this.consumerFactory = consumerFactory;

        using var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaClientContext>();
        clusters = dbContext.Clusters.ToDictionary(cluster => cluster.Id);
    }

    #region Create

    public string Name => "Local";

    public Task<bool> ValidateConnectionAsync(string BootstrapServers)
    {
        return Task.FromResult(false);
    }

    public async Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        Validate(newCluster);

        var cluster = CreateCluster(newCluster);
        clusters.Add(cluster.Id, cluster);
        try
        {
            var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaClientContext>();
            dbContext.Clusters.Add(cluster);
            await dbContext.SaveChangesAsync();
        }
        catch (Exception e)
        {
            clusters.Remove(cluster.Id);
            logger.LogError(e, "Failed to save cluster", newCluster);
            throw;
        }

        return ToModel(cluster);
    }

    private IKafkaConsumer Connect(Entities.KafkaCluster cluster)
    {
        try
        {
            var consumer = CreateConsumer(cluster.BootstrapServers);
            consumers.Add(cluster.Id, consumer);
            return consumer;
        }
        catch (Exception e)
        {
            logger.LogError(e, "Failed to create consumer", cluster);
            throw;
        }
    }

    private static Entities.KafkaCluster CreateCluster(NewKafkaCluster newCluster)
    {
        return new Entities.KafkaCluster(
            Guid.NewGuid().ToString(),
            newCluster.Name,
            newCluster.BootstrapServers);
    }

    private IKafkaConsumer CreateConsumer(string bootstrapServers)
    {
        return consumerFactory.CreateNew(bootstrapServers);
    }
    #endregion Create

    #region Read
    public Task<IEnumerable<KafkaCluster>> GetAllClustersAsync()
    {
        Log.Information("Get all clusters");
        return Task.FromResult(clusters.Values.Select(ToModel));
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
    public async Task<KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update)
    {
        ValidateClusterId(clusterId);

        await using (var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaClientContext>())
        {
            Entities.KafkaCluster? existing = await dbContext.Clusters.FindAsync(clusterId);
            if (existing != null)
            {
                existing.Name = update.Name;
                existing.BootstrapServers = update.BootstrapServers;
            }
        }
        return await GetClusterByIdAsync(clusterId);
    }
    #endregion update

    #region Delete
    public async Task RemoveClusterByIdAsync(string clusterId)
    {
        if (!clusters.ContainsKey(clusterId))
        {
            throw new KeyNotFoundException($"Cluster with id {clusterId} not found");
        }
        var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaClientContext>();
        var cluster = await dbContext.Clusters.FindAsync(clusterId);
        if (cluster != null)
        {
            dbContext.Clusters.Remove(cluster);
            await dbContext.SaveChangesAsync();
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

    private Entities.KafkaCluster ValidateClusterId(string id)
    {
        clusters.TryGetValue(id, out var cluster);
        if (cluster == null)
        {
            throw new ArgumentException("", nameof(id));
        }
        return cluster;
    }

    private Entities.KafkaCluster validateClusterName(string name)
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
        throw new ArgumentException("Unknown cluster", nameof(clusterId));
    }
    #endregion Validations

    #region Mappers
    private KafkaCluster ToModel(Entities.KafkaCluster cluster)
    {
        return new KafkaCluster(cluster.Id, cluster.Name, cluster.BootstrapServers);
    }
    #endregion Mappers
}