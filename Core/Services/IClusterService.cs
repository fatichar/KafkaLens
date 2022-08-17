using KafkaLens.Shared.Models;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics.CodeAnalysis;

namespace KafkaLens.Core.Services
{
    public interface IClusterService
    {
        Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster);
        IEnumerable<KafkaCluster> GetAllClusters();
        KafkaCluster GetById(string id);
        Task<ActionResult<List<Message>>> GetMessagesAsync(string clusterName, string topic, FetchOptions options);
        Task<ActionResult<List<Message>>> GetMessagesAsync(string clusterName, string topic, int partition, FetchOptions options);
        IList<Topic> GetTopics([DisallowNull] string clusterName);
        Task<KafkaCluster> RemoveByIdAsync(string id);
    }
}