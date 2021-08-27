
using KafkaLens.Server.Services;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KafkaLens.Server.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ClustersController : ControllerBase
    {
        private readonly ILogger<ClustersController> _logger;
        private readonly ClustersService _clustersService;
        private readonly ClusterService _clusterService;

        public ClustersController(ILogger<ClustersController> logger, ClustersService clustersService, ClusterService clusterService)
        {
            _logger = logger;
            _clustersService = clustersService;
            _clusterService = clusterService;
        }

        [HttpPost]
        public async Task<ActionResult<KafkaCluster>> Add(NewKafkaCluster newCluster)
        {
            return CreatedAtAction(nameof(GetById), await _clustersService.AddAsync(newCluster));
        }

        [HttpGet]
        public ActionResult<IEnumerable<KafkaCluster>> GetAll()
        {
            return new JsonResult(_clustersService.GetAllClusters());
        }

        [HttpGet("{clusterId}")]
        public async Task<ActionResult<KafkaCluster>> GetById(string clusterId)
        {
            return await _clustersService.GetByIdAsync(clusterId);
        }

        [HttpDelete("{clusterId}")]
        public async Task<ActionResult<KafkaCluster>> DeleteById(string clusterId)
        {
            return await _clustersService.RemoveByIdAsync(clusterId);
        }

        [HttpGet("{clusterId}/topics")]
        public async Task<ActionResult<IEnumerable<Topic>>> GetTopicsAsync(string clusterId)
        {
            IList<Topic> topics = await _clusterService.GetTopicsAsync(clusterId);
            return new JsonResult(topics);
        }

        [HttpGet("{clusterId}/topics/{topic}/messages")]
        public async Task<ActionResult<List<Message>>> GetMessages(string clusterId, string topic, int limit)
        {
            return await _clusterService.GetMessages(clusterId, topic, limit);
        }
    }
}
