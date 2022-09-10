using System.Collections.ObjectModel;
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

        KafkaCluster GetClusterById(string clusterId);

        KafkaCluster GetClusterByName(string name);

        Task<IList<Topic>> GetTopicsAsync(string clusterId);

        MessageStream GetMessagesAsync(string clusterId, string topic, FetchOptions options);

        MessageStream GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options);
        #endregion read

        #region update
        KafkaCluster UpdateCluster(string clusterId, KafkaClusterUpdate update);
        #endregion update

        #region delete
        Task<KafkaCluster> RemoveClusterByIdAsync(string clusterId);
        #endregion delete
    }
}