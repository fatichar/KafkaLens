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
        private readonly KafkaContext dbContext;
        private readonly IDictionary<string, IKafkaConsumer> consumers;

        public DbSet<Entities.KafkaCluster> Clusters => dbContext.KafkaClusters;

        public ClusterService([NotNull] ILogger<ClusterService> logger, [NotNull] KafkaContext dbContext)
        {
            this.logger = logger;
            this.dbContext = dbContext;

            // This class has singleton scope, so it is created only once.
            // So, delaying the constructor to create all the consumers is not that bad
            consumers = CreateConsumers(dbContext.KafkaClusters.ToList());
        }

        #region Create
        private Dictionary<string, IKafkaConsumer> CreateConsumers(List<Entities.KafkaCluster> clusters)
        {
            var consumers = new Dictionary<string, IKafkaConsumer>();
            clusters.ForEach(cluster =>
            {
                CreateAndAddConsumer(cluster, consumers);
            });
            return consumers;
        }

        private void CreateAndAddConsumer(Entities.KafkaCluster cluster, Dictionary<string, IKafkaConsumer> consumers)
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
        }

        public async Task<KafkaCluster?> AddAsync(NewKafkaCluster newCluster)
        {
            Validate(newCluster);

            var cluster = CreateCluster(newCluster);
            await Clusters.AddAsync(cluster);
            try
            {
                var consumer = CreateConsumer(cluster.BootstrapServers);
                consumers.Add(cluster.Name, consumer);
            }
            catch (Exception e)
            {
                logger.LogError("Failed to create consumer", newCluster, e);
                return null;
            }
            try
            {
                await dbContext.SaveChangesAsync();
            }
            catch (Exception e)
            {
                consumers.Remove(cluster.Name);
                logger.LogError("Failed to save consumer", newCluster, e);
                return null;
            }

            return ToModel(cluster);
        }

        public static Entities.KafkaCluster CreateCluster(NewKafkaCluster newCluster)
        {
            var cluster = new Entities.KafkaCluster(Guid.NewGuid().ToString(), newCluster.Name, newCluster.BootstrapServers);

            return cluster;
        }

        private static IKafkaConsumer CreateConsumer(string bootstrapServers)
        {
            return ConfluentConsumer.Create(bootstrapServers);
        }
        #endregion Create

        #region Read
        public IEnumerable<KafkaCluster> GetAllClusters()
        {
            Log.Information("Get all clusters");
            return Clusters.Select(ToModel);
        }

        public async Task<KafkaCluster> GetByIdAsync(string id)
        {
            var cluster = await GetClusterById(id);
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
            var cluster = await GetClusterById(id);
            Clusters.Remove(cluster);
            await dbContext.SaveChangesAsync();
            return ToModel(cluster);
        }
        #endregion

        #region Validations
        private void Validate(NewKafkaCluster newCluster)
        {
            var all = Clusters.ToList();

            var existing = Clusters.FirstOrDefault(cluster => cluster.Name.Equals(newCluster.Name));

            if (existing != null)
            {
                throw new ArgumentException($"Cluster with name {existing.Name} already exists");
            }
        }

        private async Task<Entities.KafkaCluster> GetClusterById(string id)
        {
            var cluster = await Clusters.FindAsync(id);
            if (cluster == null)
            {
                throw new ArgumentException("", nameof(id));
            }
            return cluster;
        }

        private async Task<Entities.KafkaCluster> GetClusterByName(string name)
        {
            var cluster = await Clusters
                .Where(cluster => cluster.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
                .FirstOrDefaultAsync();
            if (cluster == null)
            {
                throw new ArgumentException($"Cluster with name {name} does not exist", nameof(name));
            }
            return cluster;
        }

        [return: NotNull]
        private IKafkaConsumer GetConsumer(string clusterName)
        {
            if (consumers.TryGetValue(clusterName, out var consumer))
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
