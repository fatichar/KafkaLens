using KafkaLens.Client.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;

namespace KafkaLens.Client.DataAccess
{
    public class KafkaContext
    {
        private const string BASE_URL = "Clusters";
        private readonly HttpClient http;
        private Task _getClustersTask;

        private IDictionary<string, KafkaCluster> Clusters { get; set; }

        public KafkaContext(HttpClient http)
        {
            this.http = http;
            _getClustersTask = Refresh();
        }

        private async Task Refresh()
        {
            var clusters = await http.GetFromJsonAsync<IEnumerable<KafkaCluster>>("Clusters");

            SetClusters(clusters);
        }

        public async Task<IDictionary<string, KafkaCluster>> GetAllClustersAsync()
        {
            if (Clusters == null)
            {
                await _getClustersTask;
            }
            return Clusters;
        }

        private void SetClusters(IEnumerable<KafkaCluster> clusters)
        {
            Clusters = clusters.ToDictionary(cluster => cluster.Id, cluster => ToViewModel(cluster));
        }

        public async Task<KafkaCluster> GetByIdAsync(string clusterId)
        {
            if (Clusters == null)
            {
                await _getClustersTask;
            }
            return Clusters.TryGetValue(clusterId, out var cluster) ? cluster : null;
        }

        internal async Task<IList<INode>> GetTopicsAsync(string clusterId)
        {
            var cluster = GetByIdAsync(clusterId);
            var topics = await http.GetFromJsonAsync<IEnumerable<KafkaLens.Shared.Models.Topic>>($"{BASE_URL}/{clusterId}/topics");

            var topicsList = topics.Select(topic => (INode)ToViewModel(topic, clusterId)).ToList();
            return topicsList;
        }

        public async Task<KafkaCluster> AddAsync(KafkaLens.Shared.Models.NewKafkaCluster newCluster)
        {
            var response = await http.PostAsJsonAsync(BASE_URL, newCluster);
            var cluster = await response.Content.ReadFromJsonAsync<KafkaCluster>();
            Clusters.Add(cluster.Id, cluster);
            return cluster;
        }

        public async Task<bool> RemoveAsync(String clusterId)
        {
            var response = await http.DeleteAsync(BASE_URL + "/" + clusterId);
            if (response.IsSuccessStatusCode)
            {
                Clusters.Remove(clusterId);
            }
            return response.IsSuccessStatusCode;
        }

        #region converters
        private KafkaCluster ToViewModel(KafkaCluster cluster)
        {
            return new KafkaCluster(cluster.Id, cluster.Name, cluster.BootstrapServers);
        }

        private Topic ToViewModel(KafkaLens.Shared.Models.Topic topic, string clusterId)
        {
            return new Topic(topic.Name, topic.PartitionCount, clusterId);
        }
        #endregion converters
    }
}
