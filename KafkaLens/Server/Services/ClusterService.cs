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
            using (var scope = _scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<KafkaContext>();

                await db.KafkaClusters.ForEachAsync(async cluster =>
                {
                    _consumers.Add(cluster.Id, await CreateConsumerAsync(cluster));
                });
            }
        }

        private async Task<IKafkaConsumer> CreateConsumerAsync(Entities.KafkaCluster cluster)
        {
            return new ConfluentConsumer(cluster.BootstrapServers);
        }

        internal async Task<IList<Topic>> GetTopicsAsync(string clusterId)
        {
            Validate(clusterId, out var consumer);
            return consumer.GetTopics();
        }

        internal Task<ActionResult<List<Message>>> GetMessages(string clusterId, string topic1, int limit)
        {
            throw new NotImplementedException();
        }

        #region validations
        private void Validate(string clusterId, out IKafkaConsumer consumer)
        {
            if (!_consumers.TryGetValue(clusterId, out consumer))
            {
                throw new ArgumentException("", nameof(clusterId));
            }
        }
        #endregion validations
    }
}
