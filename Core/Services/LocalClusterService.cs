using KafkaLens.Core.DataAccess;
using KafkaLens.Core.Utils;
using KafkaLens.Shared.Models;
using Serilog;
using System.Diagnostics.CodeAnalysis;

namespace KafkaLens.Core.Services
{
    public class LocalClusterService : IClusterService
    {
        private readonly ILogger<LocalClusterService> logger;
        private readonly IServiceScopeFactory scopeFactory;
        private readonly ConsumerFactory consumerFactory;

        // key = cluster id, value = kafka cluster
        private Dictionary<string, Entities.KafkaCluster> clusters;

        // key = cluster id, value = kafka consumer
        private IDictionary<string, IKafkaConsumer> consumers = new Dictionary<string, IKafkaConsumer>();

        public LocalClusterService(
            [NotNull] ILogger<LocalClusterService> logger,
            [NotNull] IServiceScopeFactory scopeFactory,
            [NotNull] ConsumerFactory consumerFactory)
        {
            this.logger = logger;
            this.scopeFactory = scopeFactory;
            this.consumerFactory = consumerFactory;

            using (var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaContext>())
            {
                clusters = dbContext.KafkaClusters.ToDictionary(cluster => cluster.Id);
            }
        }

        #region Create
        public async Task<bool> ValidateConnectionAsync(string BootstrapServers)
        {
            return false;
        }

        public async Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
        {
            Validate(newCluster);

            var cluster = CreateCluster(newCluster);
            clusters.Add(cluster.Id, cluster);
            try
            {
                var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaContext>();
                dbContext.KafkaClusters.Add(cluster);
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
            return clusters.Values.Select(ToModel);
        }

        public KafkaCluster GetClusterById(string clusterId)
        {
            var cluster = ValidateClusterId(clusterId);
            return ToModel(cluster);
        }

        KafkaCluster IClusterService.GetClusterByName(string name)
        {
            var cluster = ValidateClusterId(name);
            return ToModel(cluster);
        }

        public async Task<IList<Topic>> GetTopicsAsync([DisallowNull] string clusterId)
        {
            var consumer = GetConsumer(clusterId);

            var topics = consumer.GetTopics();
            topics.Sort(Helper.CompareTopics);

            return topics;
        }

        public async Task<List<Message>> GetMessagesAsync(
            string clusterId,
            string topic,
            FetchOptions options)
        {
            var consumer = GetConsumer(clusterId);
            var messages = await consumer.GetMessagesAsync(topic, options);
            messages.Sort((m1, m2) => (int)(m1.EpochMillis - m2.EpochMillis));
            return messages;
        }

        public async Task<List<Message>> GetMessagesAsync(
            string clusterId,
            string topic,
            int partition,
            FetchOptions options)
        {
            var consumer = GetConsumer(clusterId);
            var messages = await consumer.GetMessagesAsync(topic, partition, options);
            messages.Sort((m1, m2) => (int)(m1.EpochMillis - m2.EpochMillis));
            return messages;
        }
        #endregion Read

        #region update
        public KafkaCluster UpdateCluster(string clusterId, KafkaClusterUpdate update)
        {
            ValidateClusterId(clusterId);

            using (var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaContext>())
            {
                Entities.KafkaCluster? existing = dbContext.KafkaClusters.Find(clusterId);
                if (existing != null)
                {
                    existing.Name = update.Name;
                    existing.BootstrapServers = update.BootstrapServers;
                }
            }
            return GetClusterById(clusterId);
        }
        #endregion update

        #region Delete
        public async Task<KafkaCluster> RemoveClusterByIdAsync(string clusterId)
        {
            if (clusters.ContainsKey(clusterId))
            {
                var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaContext>();
                var cluster = dbContext.KafkaClusters.Find(clusterId);
                if (cluster != null)
                {
                    dbContext.KafkaClusters.Remove(cluster);
                    await dbContext.SaveChangesAsync();
                }
                cluster = clusters[clusterId];
                clusters.Remove(clusterId);
                return ToModel(cluster);
            }
            throw new KeyNotFoundException($"Cluster with id {clusterId} not found");
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
                .Where(cluster => cluster.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                .FirstOrDefault();
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
}
