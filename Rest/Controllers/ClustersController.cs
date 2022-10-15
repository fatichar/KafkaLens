using KafkaLens.Core.Services;
using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace KafkaLens.RestApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClustersController : ControllerBase
{
    private readonly ILogger<ClustersController> logger;
    private readonly IClusterService clusterService;

    public ClustersController(ILogger<ClustersController> logger, IClusterService clusterService)
    {
        this.logger = logger;
        this.clusterService = clusterService;
    }

    [HttpPost]
    public async Task<ActionResult<KafkaCluster>> Add(NewKafkaCluster newCluster)
    {
        try
        {
            var cluster = await clusterService.AddAsync(newCluster);
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
        return new JsonResult(clusterService.GetAllClusters());
    }

    [HttpGet("{clusterId}")]
    public ActionResult<KafkaCluster> GetById(string clusterId)
    {
        try
        {
            return clusterService.GetClusterById(clusterId);
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
            return await clusterService.RemoveClusterByIdAsync(clusterId);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{clusterId}/topics")]
    public ActionResult<IEnumerable<Topic>> GetTopics(string clusterId)
    {
        try
        {
            IList<Topic> topics = (IList<Topic>)clusterService.GetTopics(clusterId);
            return new JsonResult(topics);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{clusterId}/{topic}/messages")]
    public async Task<ActionResult<List<Message>>> GetMessages(string clusterId, string topic, [FromQuery] int? limit)
    {
        try
        {
            return await clusterService.GetMessagesAsync(clusterId, topic, new FetchOptions(FetchPosition.END, limit ?? 10));
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet("{clusterId}/{topic}/{partition:int}/messages")]
    public async Task<ActionResult<List<Message>>> GetMessages(string clusterId, string topic, int partition, [FromQuery] int? limit)
    {
        try
        {
            return await clusterService.GetMessagesAsync(clusterId, topic, partition, new FetchOptions(FetchPosition.END, limit ?? 10));
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}