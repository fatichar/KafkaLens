using System.Collections.Generic;
using System.Threading.Tasks;
using KafkaLens.Shared.Models;

namespace KafkaLens.Shared;

public interface IKafkaLensClient
{
    #region create
    Task<bool> ValidateConnectionAsync(string bootstrapServers);

    Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster);
    #endregion create

    #region read
    Task<IEnumerable<KafkaCluster>> GetAllClustersAsync();

    Task<KafkaCluster> GetClusterByIdAsync(string clusterId);

    Task<KafkaCluster> GetClusterByNameAsync(string name);

    Task<IList<Topic>> GetTopicsAsync(string clusterId);

    MessageStream GetMessageStream(string clusterId, string topic, FetchOptions options);

    Task<List<Message>> GetMessagesAsync(string clusterId, string topic, FetchOptions options);

    MessageStream GetMessageStream(string clusterId, string topic, int partition, FetchOptions options);
    Task<List<Message>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options);
    #endregion read

    #region update
    Task<KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update);
    #endregion update

    #region delete
    Task RemoveClusterByIdAsync(string clusterId);
    #endregion delete
}