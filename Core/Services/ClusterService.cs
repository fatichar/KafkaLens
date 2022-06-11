using KafkaLens.Core.DataAccess;
using KafkaLens.Core.Utils;
using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Diagnostics.CodeAnalysis;

namespace KafkaLens.Core.Services
{
    public class ClusterService
    {
        private readonly ILogger<ClusterService> logger;
        private readonly IServiceScopeFactory scopeFactory;
        private readonly ConsumerFactory consumerFactory;

        private IDictionary<string, IKafkaConsumer> Consumers { get; }

        public Dictionary<string, Entities.KafkaCluster> Clusters { get; }

        public ClusterService(
            [NotNull] ILogger<ClusterService> logger, 
            [NotNull] IServiceScopeFactory scopeFactory, 
            [NotNull] ConsumerFactory consumerFactory)
        {
            this.logger = logger;
            this.scopeFactory = scopeFactory;
            this.consumerFactory = consumerFactory;

            var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaContext>();
            Clusters = dbContext.KafkaClusters.ToDictionary(cluster => cluster.Name);
            Consumers = CreateConsumers(Clusters.Values.ToList());
        }

        #region Create
        private Dictionary<string, IKafkaConsumer> CreateConsumers(List<Entities.KafkaCluster> clusters)
        {
            var consumers = new Dictionary<string, IKafkaConsumer>();
            clusters.ForEach(cluster =>
            {
                try
                {
                    var consumer = CreateConsumer(cluster.BootstrapServers);
                    consumers.Add(cluster.Name, consumer);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to create consumer");
                }
            });
            return consumers;
        }

        public async Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
        {
            Validate(newCluster);

            var cluster = CreateCluster(newCluster);
            Clusters.Add(cluster.Id, cluster);
            try
            {
                var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaContext>();
                dbContext.KafkaClusters.Add(cluster);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception e)
            {
                Clusters.Remove(cluster.Id);
                logger.LogError(e, "Failed to save cluster", newCluster);
                throw;
            }
            try
            {
                var consumer = CreateConsumer(cluster.BootstrapServers);
                Consumers.Add(cluster.Name, consumer);
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create consumer", newCluster);
            }

            return ToModel(cluster);
        }

        public static Entities.KafkaCluster CreateCluster(NewKafkaCluster newCluster)
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
        public IEnumerable<KafkaCluster> GetAllClusters()
        {
            Log.Information("Get all clusters");
            return Clusters.Values.Select(ToModel);
        }

        public KafkaCluster GetById(string id)
        {
            var cluster = GetClusterById(id);
            return ToModel(cluster);
        }

        public async Task<IList<Topic>> GetTopicsAsync([DisallowNull] string clusterName)
        {
            var consumer = GetConsumer(clusterName);

            var topics = await consumer.GetTopicsAsync();
            topics.Sort(Helper.CompareTopics);

            return topics;
        }

        public async Task<ActionResult<List<Message>>> GetMessagesAsync(
            string clusterName,
            string topic,
            FetchOptions options)
        {
            var consumer = GetConsumer(clusterName);
            var messages = await consumer.GetMessagesAsync(topic, options);
            messages.Sort((m1, m2) => (int)(m1.EpochMillis - m2.EpochMillis));
            return messages;
        }

        public async Task<ActionResult<List<Message>>> GetMessagesAsync(
            string clusterName,
            string topic,
            int partition,
            FetchOptions options)
        {
            var consumer = GetConsumer(clusterName);
            var messages = await consumer.GetMessagesAsync(topic, partition, options);
            messages.Sort((m1, m2) => (int)(m1.EpochMillis - m2.EpochMillis));
            return messages;
        }
        #endregion Read
        #region Delete
        public async Task<KafkaCluster> RemoveByIdAsync(string id)
        {
            if (Clusters.ContainsKey(id))
            {
                var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaContext>();
                var cluster = dbContext.KafkaClusters.Find(id);
                if (cluster != null)
                {
                    dbContext.KafkaClusters.Remove(cluster);
                    await dbContext.SaveChangesAsync();
                }
                cluster = Clusters[id];
                Clusters.Remove(id);
                return ToModel(cluster);
            }
            throw new KeyNotFoundException($"Cluster with id {id} not found");
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

        private Entities.KafkaCluster GetClusterById(string id)
        {
            Clusters.TryGetValue(id, out var cluster);
            if (cluster == null)
            {
                throw new ArgumentException("", nameof(id));
            }
            return cluster;
        }

        private Entities.KafkaCluster GetClusterByName(string name)
        {
            var cluster = Clusters.Values
                .Where(cluster => cluster.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                .FirstOrDefault();
            if (cluster == null)
            {
                throw new ArgumentException($"Cluster with name {name} does not exist", nameof(name));
            }
            return cluster;
        }

        [return: NotNull]
        private IKafkaConsumer GetConsumer(string clusterName)
        {
            if (Consumers.TryGetValue(clusterName, out var consumer))
            {
                return consumer;
            }
            throw new ArgumentException("", nameof(clusterName));
        }
        #endregion Validations
        #region Mappers
        private KafkaCluster ToModel(Entities.KafkaCluster cluster)
        {
            return new KafkaCluster(cluster.Id, cluster.Name, cluster.BootstrapServers);
        }

        private Entities.KafkaCluster ToEntity(KafkaCluster cluster)
        {
            return new Entities.KafkaCluster(cluster.Id, cluster.Name, cluster.BootstrapServers);
        }
        #endregion Mappers
    }
}
