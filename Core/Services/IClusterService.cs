using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;

namespace KafkaLens.Core.Services
{
    public interface IClusterService
    {
        #region create
        Task<KafkaCluster> ValidateConnectionAsync(string BootstrapServers);
        
        Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster);
        #endregion create

        #region read
        IEnumerable<KafkaCluster> GetAllClusters();

        KafkaCluster GetClusterById(string clusterId);

        KafkaCluster GetClusterByName(string name);

        Task<IList<Topic>> GetTopicsAsync([DisallowNull] string clusterId);
        
        Task<ActionResult<List<Message>>> GetMessagesAsync(string clusterId, string topic, FetchOptions options);
        
        Task<ActionResult<List<Message>>> GetMessagesAsync(string clusterId, string topic, int partition, FetchOptions options);
        #endregion read

        #region update
        KafkaCluster UpdateCluster(string clusterId, KafkaClusterUpdate update);
        #endregion update

        #region delete
        Task<KafkaCluster> RemoveClusterByIdAsync(string clusterId);
        #endregion delete
    }
}