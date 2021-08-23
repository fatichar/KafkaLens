﻿using KafkaLens.Shared.Models;
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
        private readonly HttpClient http;
        public KafkaContext(HttpClient http)
        {
            this.http = http;
        }

        private IDictionary<string, KafkaCluster> Clusters { get; set; }

        public async Task<List<KafkaCluster>> GetAllClustersAsync()
        {
            if (Clusters == null)
            {
                var clusters = await http.GetFromJsonAsync<IEnumerable<KafkaCluster>>("KafkaCluster");

                SetClusters(clusters);
            }
            return Clusters.Values.ToList();
        }

        public KafkaCluster GetById(string id)
        {
            return Clusters.TryGetValue(id, out KafkaCluster cluster) ? cluster : null;
        }

        public void SetClusters(IEnumerable<KafkaCluster> clusters)
        {
            Clusters = clusters.ToDictionary(cluster => cluster.Id, cluster => cluster);
        }

        public async Task<KafkaCluster> AddAsync(NewKafkaCluster newCluster)
        {
            var response = await http.PostAsJsonAsync("KafkaCluster", newCluster);
            var cluster = await response.Content.ReadFromJsonAsync<KafkaCluster>();
            Clusters.Add(cluster.Id, cluster);
            return cluster;
        }

        public bool Remove(String id)
        {
            return Clusters.Remove(id);
        }
    }
}
