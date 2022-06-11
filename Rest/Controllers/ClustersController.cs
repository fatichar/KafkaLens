
using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;

using Microsoft.AspNetCore.Mvc;

namespace KafkaLens.Rest.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ClustersController : ControllerBase
    {
        private readonly ILogger<ClustersController> _logger;
        private readonly ClusterService _clusterService;

        public ClustersController(ILogger<ClustersController> logger, ClusterService clusterService)
        {
            _logger = logger;
            _clusterService = clusterService;
        }

        [HttpPost]
        public async Task<ActionResult<KafkaCluster>> Add(NewKafkaCluster newCluster)
        {
            try
            {
                var cluster = await _clusterService.AddAsync(newCluster);
                return base.CreatedAtAction(nameof(GetById), cluster.Name, cluster);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet]
        public ActionResult<IEnumerable<KafkaCluster>> GetAll()
        {
            return new JsonResult(_clusterService.GetAllClusters());
        }

        [HttpGet("{clusterId}")]
        public ActionResult<KafkaCluster> GetById(string clusterId)
        {
            try
            {
                return _clusterService.GetById(clusterId);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpDelete("{clusterId}")]
        public async Task<ActionResult<KafkaCluster>> DeleteById(string clusterId)
        {
            try
            {
                return await _clusterService.RemoveByIdAsync(clusterId);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{clusterId}/topics")]
        public async Task<ActionResult<IEnumerable<Topic>>> GetTopicsAsync(string clusterId)
        {
            try
            {
                IList<Topic> topics = await _clusterService.GetTopicsAsync(clusterId);
                return new JsonResult(topics);
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{clusterId}/{topic}/messages")]
        public async Task<ActionResult<List<Message>>> GetMessages(string clusterId, string topic, [FromQuery] int limit)
        {
            try
            {
                return await _clusterService.GetMessagesAsync(clusterId, topic, new FetchOptions() { Limit = limit });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }

        [HttpGet("{clusterId}/{topic}/{partition}/messages")]
        public async Task<ActionResult<List<Message>>> GetMessages(string clusterId, string topic, int partition, [FromQuery] int limit)
        {
            try
            {
                return await _clusterService.GetMessagesAsync(clusterId, topic, partition, new FetchOptions() { Limit = limit });
            }
            catch (Exception ex)
            {
                return BadRequest(ex.Message);
            }
        }
    }
}
