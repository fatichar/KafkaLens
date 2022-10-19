using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services;

public class RestClusterService : IClusterService
{
    #region Fields
    
    private readonly ILogger<RestClusterService> logger;
    
    #endregion

    #region Constructors

    public RestClusterService(ILogger<RestClusterService> logger)
    {
        this.logger = logger;
    }

    #endregion

    public async Task<bool> ValidateConnectionAsync(string BootstrapServers)
    {
        throw new NotImplementedException();
    }

    #region Create

    public async Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Read

    public IEnumerable<KafkaCluster> GetAllClusters()
    {
        throw new NotImplementedException();
    }

    public KafkaCluster GetClusterById(string clusterId)
    {
        throw new NotImplementedException();
    }

    public KafkaCluster GetClusterByName(string name)
    {
        throw new NotImplementedException();
    }

    public IList<Topic> GetTopics(string clusterId)
    {
        throw new NotImplementedException();
    }

    public MessageStream GetMessageStream(string clusterId, string topic, FetchOptions options)
    {
        throw new NotImplementedException();
    }

    public async Task<List<Message>> GetMessagesAsync(string clusterId, string topic, FetchOptions options)
    {
        throw new NotImplementedException();
    }

    public MessageStream GetMessageStream(string clusterId, string topic, int partition, FetchOptions options)
    {
        throw new NotImplementedException();
    }

    public async Task<List<Message>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Update

    public KafkaCluster UpdateCluster(string clusterId, KafkaClusterUpdate update)
    {
        throw new NotImplementedException();
    }

    #endregion

    #region Delete

    public async Task<KafkaCluster> RemoveClusterByIdAsync(string clusterId)
    {
        throw new NotImplementedException();
    }

    #endregion
}