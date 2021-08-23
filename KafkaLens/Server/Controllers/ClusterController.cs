using KafkaLens.Server.Services;
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
    public class ClusterController : ControllerBase
    {
        private readonly ILogger<ClustersController> _logger;
        private readonly ClusterService _service;

        public ClusterController(ILogger<ClustersController> logger, ClusterService service)
        {
            _logger = logger;
            _service = service;
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<List<Message>>> GetMessages(string topic, int limit)
        {
            return await _service.GetMessages(topic, limit);
        }

        [HttpGet("{clusterId}")]
        public ActionResult<IEnumerable<Topic>> GetTopics(string clusterId)
        {
            return new JsonResult(_service.GetTopics(clusterId));
        }
    }
}
