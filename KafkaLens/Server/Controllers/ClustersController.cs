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
        private readonly ClustersService _service;

        public ClustersController(ILogger<ClustersController> logger, ClustersService service)
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

        [HttpDelete("{id}")]
        public async Task<ActionResult<KafkaCluster>> DeleteById(string id)
        {
            return await _service.RemoveByIdAsync(id);
        }
    }
}
