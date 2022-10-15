using KafkaLens.Shared.Models;

namespace KafkaLens.Core.Services;

public interface IClusterService
{
    #region create
    Task<bool> ValidateConnectionAsync(string BootstrapServers);

    Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster);
    #endregion create

    #region read
    IEnumerable<KafkaCluster> GetAllClusters();

    KafkaCluster GetClusterById(string clusterId);

    KafkaCluster GetClusterByName(string name);

    IList<Topic> GetTopics(string clusterId);

    MessageStream GetMessageStream(string clusterId, string topic, FetchOptions options);

    Task<List<Message>> GetMessagesAsync(string clusterId, string topic, FetchOptions options);

    MessageStream GetMessageStream(string clusterId, string topic, int partition, FetchOptions options);
    Task<List<Message>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options);
    #endregion read

    #region update
    KafkaCluster UpdateCluster(string clusterId, KafkaClusterUpdate update);
    #endregion update

    #region delete
    Task<KafkaCluster> RemoveClusterByIdAsync(string clusterId);
    #endregion delete
}