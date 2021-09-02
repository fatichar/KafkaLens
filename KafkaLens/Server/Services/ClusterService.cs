using KafkaLens.Server.DataAccess;
using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;

namespace KafkaLens.Server.Services
{
    public class ClusterService
    {
        private readonly ILogger<ClusterService> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IDictionary<string, IKafkaConsumer> _consumers = new Dictionary<string, IKafkaConsumer>();

        public ClusterService(ILogger<ClusterService> logger, IServiceScopeFactory scopeFactory)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _ = CreateConsumersAsync();
        }

        private async Task CreateConsumersAsync()
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KafkaContext>();
            await db.KafkaClusters.ForEachAsync(cluster => _consumers.Add(cluster.Name, CreateConsumer(cluster)));
        }

        private static IKafkaConsumer CreateConsumer(Entities.KafkaCluster cluster)
        {
            return new ConfluentConsumer(cluster.BootstrapServers);
        }

        public async Task<IList<Topic>> GetTopicsAsync(string clusterName)
        {
            Validate(clusterName, out var consumer);

            var topics = await consumer.GetTopicsAsync();
            topics.Sort(CompareTopics);

            return topics;
        }

        public async Task<ActionResult<List<Message>>> GetMessagesAsync(
            string clusterName, 
            string topic,
            FetchOptions options)
        {
            Validate(clusterName, out var consumer);
            return await consumer.GetMessagesAsync(topic, options);
        }

        public async Task<ActionResult<List<Message>>> GetMessagesAsync(
            string clusterName,
            string topic,
            int partition,
            FetchOptions options)
        {
            Validate(clusterName, out var consumer);
            return await consumer.GetMessagesAsync(topic, partition, options);
        }

        #region Validations
        private void Validate(string clusterName, out IKafkaConsumer consumer)
        {
            if (!_consumers.TryGetValue(clusterName, out consumer))
            {
                throw new ArgumentException("", nameof(clusterName));
            }
        }
        #endregion Validations

        #region Helpers
        private int CompareTopics(Topic x, Topic y)
        {
            int underScoresX = CountPrefix(x.Name, '_');
            int underScoresY = CountPrefix(y.Name, '_');
            if (underScoresX == underScoresY)
            {
                return x.Name.CompareTo(y.Name);
            }
            return underScoresX < underScoresY ? -1 : +1;
        }

        private int CountPrefix(string s, char c)
        {
            int i = 0;
            while (i < s.Length && s[i] == c)
            {
                ++i;
            }
            return i;
        }
        #endregion Helpers
    }
}
