﻿//using KafkaLens.Core.DataAccess;
//using KafkaLens.Core.Utils;
//using KafkaLens.Shared.Models;
//using Microsoft.AspNetCore.Mvc;
//using Microsoft.EntityFrameworkCore;
//using Serilog;
//using System.Diagnostics.CodeAnalysis;

//namespace KafkaLens.Core.Services
//{
//    public class SharedClusterService : IClusterService
//    {
//        private readonly ILogger<SharedClusterService> logger;
//        private readonly IServiceScopeFactory scopeFactory;
//        private readonly ConsumerFactory consumerFactory;

//        private IDictionary<string, IKafkaConsumer> consumers;

//        private Dictionary<string, Entities.ClusterInfo> clusters;

//        public SharedClusterService(
//            [NotNull] ILogger<SharedClusterService> logger,
//            [NotNull] IServiceScopeFactory scopeFactory,
//            [NotNull] ConsumerFactory consumerFactory)
//        {
//            this.logger = logger;
//            this.scopeFactory = scopeFactory;
//            this.consumerFactory = consumerFactory;

//            var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaContext>();
//            clusters = dbContext.KafkaClusters.ToDictionary(cluster => cluster.Name);
//            consumers = CreateConsumers(clusters.Values.ToList());
//        }

//        #region Create
//        private Dictionary<string, IKafkaConsumer> CreateConsumers(List<Entities.ClusterInfo> clusters)
//        {
//            var consumers = new Dictionary<string, IKafkaConsumer>();
//            clusters.ForEach(cluster =>
//            {
//                try
//                {
//                    var consumer = CreateConsumer(cluster.Address);
//                    consumers.Add(cluster.Name, consumer);
//                }
//                catch (Exception ex)
//                {
//                    logger.LogError(ex, "Failed to create consumer");
//                }
//            });
//            return consumers;
//        }

//        public async Task<ClusterInfo> AddAsync(NewKafkaCluster newCluster)
//        {
//            Validate(newCluster);

//            var cluster = CreateCluster(newCluster);
//            clusters.Add(cluster.Id, cluster);
//            try
//            {
//                var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaContext>();
//                dbContext.KafkaClusters.Add(cluster);
//                await dbContext.SaveChangesAsync();
//            }
//            catch (Exception e)
//            {
//                clusters.Remove(cluster.Id);
//                logger.LogError(e, "Failed to save cluster", newCluster);
//                throw;
//            }
//            try
//            {
//                var consumer = CreateConsumer(cluster.Address);
//                consumers.Add(cluster.Name, consumer);
//            }
//            catch (Exception e)
//            {
//                logger.LogError(e, "Failed to create consumer", newCluster);
//            }

//            return ToModel(cluster);
//        }

//        public static Entities.ClusterInfo CreateCluster(NewKafkaCluster newCluster)
//        {
//            return new Entities.ClusterInfo(
//                Guid.NewGuid().ToString(),
//                newCluster.Name,
//                newCluster.Address);
//        }

//        private IKafkaConsumer CreateConsumer(string bootstrapServers)
//        {
//            return consumerFactory.CreateNew(bootstrapServers);
//        }
//        #endregion Create

//        #region Read
//        public IEnumerable<ClusterInfo> GetAllClusters()
//        {
//            Log.Information("Get all clusters");
//            return clusters.Values.Select(ToModel);
//        }

//        public ClusterInfo GetClusterById(string id)
//        {
//            var cluster = GetClusterById(id);
//            return ToModel(cluster);
//        }

//        public IList<Topic> GetTopicsAsync([DisallowNull] string clusterName)
//        {
//            var consumer = GetConsumer(clusterName);

//            var topics = consumer.GetTopics();
//            topics.Sort(Helper.CompareTopics);

//            return topics;
//        }

//        public async Task<ActionResult<List<Message>>> GetMessagesAsync(
//            string clusterName,
//            string topic,
//            FetchOptions options)
//        {
//            var consumer = GetConsumer(clusterName);
//            var messages = consumer.GetMessages(topic, options);
//            messages.Sort((m1, m2) => (int)(m1.EpochMillis - m2.EpochMillis));
//            return messages;
//        }

//        public async Task<ActionResult<List<Message>>> GetMessagesAsync(
//            string clusterName,
//            string topic,
//            int partition,
//            FetchOptions options)
//        {
//            var consumer = GetConsumer(clusterName);
//            var messages = await consumer.GetMessagesAsync(topic, partition, options);
//            messages.Sort((m1, m2) => (int)(m1.EpochMillis - m2.EpochMillis));
//            return messages;
//        }
//        #endregion Read
//        #region Delete
//        public async Task<ClusterInfo> RemoveByIdAsync(string id)
//        {
//            if (clusters.ContainsKey(id))
//            {
//                var dbContext = scopeFactory.CreateScope().ServiceProvider.GetRequiredService<KafkaContext>();
//                var cluster = dbContext.KafkaClusters.Find(id);
//                if (cluster != null)
//                {
//                    dbContext.KafkaClusters.Remove(cluster);
//                    await dbContext.SaveChangesAsync();
//                }
//                cluster = clusters[id];
//                clusters.Remove(id);
//                return ToModel(cluster);
//            }
//            throw new KeyNotFoundException($"Cluster with id {id} not found");
//        }
//        #endregion

//        #region Validations
//        private void Validate(NewKafkaCluster newCluster)
//        {
//            var all = clusters.ToList();

//            var existing = clusters.Values.FirstOrDefault(cluster =>
//                    cluster.Name.Equals(newCluster.Name, StringComparison.InvariantCultureIgnoreCase));

//            if (existing != null)
//            {
//                throw new ArgumentException($"Cluster with name {existing.Name} already exists");
//            }
//        }

//        private Entities.ClusterInfo GetClusterById(string id)
//        {
//            clusters.TryGetValue(id, out var cluster);
//            if (cluster == null)
//            {
//                throw new ArgumentException("", nameof(id));
//            }
//            return cluster;
//        }

//        private Entities.ClusterInfo GetClusterByName(string name)
//        {
//            var cluster = clusters.Values
//                .Where(cluster => cluster.Name.Equals(name, StringComparison.CurrentCultureIgnoreCase))
//                .FirstOrDefault();
//            if (cluster == null)
//            {
//                throw new ArgumentException($"Cluster with name {name} does not exist", nameof(name));
//            }
//            return cluster;
//        }

//        [return: NotNull]
//        private IKafkaConsumer GetConsumer(string clusterName)
//        {
//            if (consumers.TryGetValue(clusterName, out var consumer))
//            {
//                return consumer;
//            }
//            throw new ArgumentException("", nameof(clusterName));
//        }
//        #endregion Validations
//        #region Mappers
//        private ClusterInfo ToModel(Entities.ClusterInfo cluster)
//        {
//            return new ClusterInfo(cluster.Id, cluster.Name, cluster.Address);
//        }

//        private Entities.ClusterInfo ToEntity(ClusterInfo cluster)
//        {
//            return new Entities.ClusterInfo(cluster.Id, cluster.Name, cluster.Address);
//        }

//        public Task<ClusterInfo> ValidateConnectionAsync(string Address)
//        {
//            throw new NotImplementedException();
//        }

//        ClusterInfo IClusterService.GetClusterByName(string name)
//        {
//            throw new NotImplementedException();
//        }

//        public ClusterInfo UpdateCluster(string id, KafkaClusterUpdate update)
//        {
//            throw new NotImplementedException();
//        }

//        public Task<ClusterInfo> RemoveClusterByIdAsync(string id)
//        {
//            throw new NotImplementedException();
//        }
//        #endregion Mappers
//    }
//}
