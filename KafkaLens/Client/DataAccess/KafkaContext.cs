using KafkaLens.Client.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace KafkaLens.Client.DataAccess
{
    public class KafkaContext
    {
        private const string BASE_URL = "Clusters";
        private readonly HttpClient _http;
        private readonly ILogger<KafkaContext> _logger;
        private Task _getClustersTask;

        private Dictionary<string, KafkaCluster> Clusters { get; set; }

        public KafkaContext(HttpClient http, ILogger<KafkaContext> logger)
        {
            this._http = http;
            _logger = logger;
            _getClustersTask = RefreshClusters();
        }

        private async Task<Dictionary<string, KafkaCluster>> RefreshClusters()
        {
            try
            {
                var clusters = await _http.GetFromJsonAsync<IEnumerable<KafkaCluster>>("Clusters");
                Clusters = clusters?.ToDictionary(cluster => cluster.Name, ToViewModel);
            }
            catch (HttpRequestException e)
            {
                _logger.LogError(e.Message);
            }

            return null;
        }

        public async Task<IDictionary<string, KafkaCluster>> GetAllClustersAsync()
        {
            await _getClustersTask;
            return Clusters;
        }

        public async Task<KafkaCluster> GetByNameAsync(string clusterName)
        {
            await _getClustersTask;
            return Clusters.TryGetValue(clusterName, out var cluster) ? cluster : null;
        }

        internal async Task<IList<INode>> GetTopicsAsync(string clustername)
        {
            try
            {
                var topics = await _http.GetFromJsonAsync<IEnumerable<KafkaLens.Shared.Models.Topic>>(
                $"{BASE_URL}/{clustername}/topics");

                var topicsList = topics?.Select(topic => (INode)ToViewModel(topic, clustername)).ToList();
                return topicsList;
            } catch (Exception e)
            {
                _logger.LogError(e.Message);
            }
            return null;
        }

        internal async Task<List<Message>> GetMessagesAsync(string clustername, string topic)
        {
            try
            {
                string requestUri = $"{BASE_URL}/{clustername}/{topic}/messages?limit=40";
                var messages = await _http.GetFromJsonAsync<IEnumerable<KafkaLens.Shared.Models.Message>>(
                    requestUri);

                return messages.Select(ToViewModel).ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
            }
            return null;
        }

        internal async Task<List<Message>> GetMessagesAsync(string clustername, string topic, int partition)
        {
            try
            {
                string requestUri = $"{BASE_URL}/{clustername}/{topic}/{partition}/messages?limit=20";
                var messages = await _http.GetFromJsonAsync<IEnumerable<KafkaLens.Shared.Models.Message>>(
                    requestUri);

                return messages.Select(ToViewModel).ToList();
            }
            catch (Exception e)
            {
                _logger.LogError(e.Message);
            }
            return null;
        }

        Message ToViewModel(KafkaLens.Shared.Models.Message message)
        {
            return new(message.Key, message.Value)
            {
                Partition = message.Partition,
                Offset = message.Offset,
                //TimeStamp = DateTime.Now
                TimeStamp = DateTimeOffset.FromUnixTimeMilliseconds(message.EpochMillis).DateTime
            };
        }

        public async Task<KafkaCluster> AddAsync(KafkaLens.Shared.Models.NewKafkaCluster newCluster)
        {
            var response = await _http.PostAsJsonAsync(BASE_URL, newCluster);
            var cluster = await response.Content.ReadFromJsonAsync<KafkaCluster>();
            if (cluster != null)
            {
                Clusters.Add(cluster.Name, cluster);
            }
            return cluster;
        }

        public async Task<bool> RemoveAsync(String clusterName)
        {
            var response = await _http.DeleteAsync(BASE_URL + "/" + clusterName);
            if (response.IsSuccessStatusCode)
            {
                Clusters.Remove(clusterName);
            }
            return response.IsSuccessStatusCode;
        }

        #region converters
        private KafkaCluster ToViewModel(KafkaCluster cluster)
        {
            return new KafkaCluster(cluster.Name, cluster.BootstrapServers);
        }

        private static Topic ToViewModel(KafkaLens.Shared.Models.Topic topic, string clusterName)
        {
            return new Topic(topic.Name, topic.PartitionCount, clusterName);
        }
        #endregion converters
    }
}
