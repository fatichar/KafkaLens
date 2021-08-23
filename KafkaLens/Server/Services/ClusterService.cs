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

namespace KafkaLens.Server.Services
{
    public class ClusterService
    {
        private readonly ILogger<ClusterService> _logger;
        private readonly KafkaContext _dbContext;
        private readonly IKafkaConsumer _consumer;

        public ClusterService(ILogger<ClusterService> logger, IKafkaConsumer consumer)
        {
            _logger = logger;
            _consumer = consumer;
        }

        internal Task<ActionResult<List<Message>>> GetMessages(string topic, int limit)
        {
            throw new NotImplementedException();
        }

        internal object GetTopics()
        {
            return _consumer.GetTopics();
        }
    }
}
