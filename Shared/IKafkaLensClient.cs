using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using KafkaLens.Shared.Models;

namespace KafkaLens.Shared;

public interface IKafkaLensClient
{
    public string Name { get; }
    
    public bool CanEditClusters { get; }
    
    public bool CanSaveMessages { get; }

    #region create
    Task<bool> ValidateConnectionAsync(string bootstrapServers);

    Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster);
    #endregion create

    #region read
    Task<IEnumerable<KafkaCluster>> GetAllClustersAsync();

    Task<KafkaCluster> GetClusterByIdAsync(string clusterId);

    Task<KafkaCluster> GetClusterByNameAsync(string name);

    Task<IList<Topic>> GetTopicsAsync(string clusterId);

    MessageStream GetMessageStream(string clusterId, string topic, FetchOptions options, CancellationToken cancellationToken = default);

    Task<List<Message>> GetMessagesAsync(string clusterId, string topic, FetchOptions options, CancellationToken cancellationToken = default);

    MessageStream GetMessageStream(string clusterId, string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default);
    Task<List<Message>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options, CancellationToken cancellationToken = default);
    #endregion read

    #region update
    Task<KafkaCluster> UpdateClusterAsync(string clusterId, KafkaClusterUpdate update);
    #endregion update

    #region delete
    Task RemoveClusterByIdAsync(string clusterId);
    #endregion delete
}