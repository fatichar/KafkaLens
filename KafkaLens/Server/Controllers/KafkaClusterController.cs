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
    public class KafkaClusterController : ControllerBase
    {
        private static readonly string[] Summaries = new[]
        {
            "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
        };

        private readonly ILogger<KafkaClusterController> _logger;
        private readonly KafkaClusterService _service;

        public KafkaClusterController(ILogger<KafkaClusterController> logger, KafkaClusterService service)
        {
            _logger = logger;
            _service = service;
        }

        [HttpPost]
        public async Task<ActionResult<KafkaCluster>> Add(NewKafkaCluster newCluster)
        {
            return CreatedAtAction(nameof(GetById), _service.Add(newCluster));
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<KafkaCluster>> GetById(string id)
        {
            return await _service.GetByIdAsync(id);
        }

        [HttpGet]
        public ActionResult<IEnumerable<KafkaCluster>> GetAll()
        {
            return new JsonResult(_service.GetAllClusters());
        }
    }
}
