using KafkaLens.Shared.Models;
using System.Diagnostics.CodeAnalysis;

namespace KafkaLens.Core.Services
{
    public interface IClusterService
    {
        #region create
        Task<bool> ValidateConnectionAsync(string BootstrapServers);
        
        Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster);
        #endregion create

        #region read
        IEnumerable<KafkaCluster> GetAllClusters();

        KafkaCluster GetClusterById([DisallowNull] string clusterId);

        KafkaCluster GetClusterByName([DisallowNull] string name);

        Task<IList<Topic>> GetTopicsAsync([DisallowNull] string clusterId);
        
        Task<List<Message>> GetMessagesAsync([DisallowNull] string clusterId, [DisallowNull] string topic, FetchOptions options);
        
        Task<List<Message>> GetMessagesAsync([DisallowNull] string clusterId, [DisallowNull] string topic, [DisallowNull] int partition, FetchOptions options);
        #endregion read

        #region update
        KafkaCluster UpdateCluster(string clusterId, KafkaClusterUpdate update);
        #endregion update

        #region delete
        Task<KafkaCluster> RemoveClusterByIdAsync(string clusterId);
        #endregion delete
    }
}