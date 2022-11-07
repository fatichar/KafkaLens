using KafkaLens.Core.Services;
using KafkaLens.Shared;
using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace KafkaLens.RestApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ClustersController : ControllerBase
{
    private readonly ILogger<ClustersController> logger;
    private readonly IKafkaLensClient kafkaLensClient;

    public ClustersController(ILogger<ClustersController> logger, IKafkaLensClient kafkaLensClient)
    {
        this.logger = logger;
        this.kafkaLensClient = kafkaLensClient;
    }

    [HttpPost]
    public async Task<ActionResult<KafkaCluster>> AddCluster(NewKafkaCluster newCluster)
    {
        try
        {
            var cluster = await kafkaLensClient.AddAsync(newCluster);
            return base.CreatedAtAction(nameof(GetClusterById), cluster.Name, cluster);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpGet]
    public ActionResult<IEnumerable<KafkaCluster>> GetAllClusters()
    {
        return new JsonResult(kafkaLensClient.GetAllClustersAsync());
    }

    [HttpGet("{clusterId}")]
    public async Task<ActionResult<KafkaCluster>> GetClusterById(string clusterId)
    {
        try
        {
            return await kafkaLensClient.GetClusterByIdAsync(clusterId);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{clusterId}")]
    public async Task<ActionResult<KafkaCluster>> DeleteClusterById(string clusterId)
    {
        try
        {
            return await kafkaLensClient.RemoveClusterByIdAsync(clusterId);
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
            IList<Topic> topics = (IList<Topic>)kafkaLensClient.GetTopicsAsync(clusterId);
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
            return await kafkaLensClient.GetMessagesAsync(clusterId, topic, new FetchOptions(FetchPosition.END, limit ?? 10));
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
            return await kafkaLensClient.GetMessagesAsync(clusterId, topic, partition, new FetchOptions(FetchPosition.END, limit ?? 10));
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }
}