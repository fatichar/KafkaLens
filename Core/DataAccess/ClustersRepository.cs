using System.Collections.ObjectModel;
using System.Text.Json;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Entities;
using Serilog;

namespace KafkaLens.Core.DataAccess;

public class ClustersRepository : IClustersRepository
{
    private const string DEFAULT_FILE_PATH = "cluster_info.json";
    private readonly string filePath;
    private Dictionary<string, KafkaCluster> clusters;
    public ReadOnlyDictionary<string, KafkaCluster> GetAll() => new(clusters);

    public ClustersRepository(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Log.Warning("Clusters config file path not specified, using default: {DefaultPath}",
                DEFAULT_FILE_PATH);
            filePath = DEFAULT_FILE_PATH;
            File.CreateText(filePath);
        }
        this.filePath = filePath;

        LoadClusters(filePath);
    }

    private void LoadClusters(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new IOException($"File {filePath} does not exist");
        }

        var configFile = File.ReadAllText(filePath);
        var clusterConfig = JsonSerializer.Deserialize<ClusterConfig>(configFile);
        clusters = clusterConfig.Clusters.ToDictionary(cluster => cluster.Id);
    }

    public KafkaCluster GetById(string id)
    {
        ValidateClusterById(id);
        return clusters[id];
    }

    private void ValidateClusterById(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            throw new Exception("Cluster id cannot be null or empty");
        }
        if (!clusters.ContainsKey(id))
        {
            throw new Exception($"Cluster with id {id} does not exist");
        }
    }

    public void Add(KafkaCluster cluster)
    {
        if (clusters.ContainsKey(cluster.Id))
        {
            throw new Exception($"Cluster with id {cluster.Id} already exists");
        }

        clusters.Add(cluster.Id, cluster);
        SaveClusters();
    }

    private void SaveClusters()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        var clusterConfig = new ClusterConfig
        {
            Clusters = clusters.Values.ToList()
        };
        var json = JsonSerializer.Serialize(clusterConfig, options);
        File.WriteAllText(filePath, json);
    }

    public void Update(KafkaCluster cluster)
    {
        ValidateClusterById(cluster.Id);
        clusters[cluster.Id] = cluster;
        SaveClusters();
    }
    
    public void Delete(string id)
    {
        ValidateClusterById(id);
        clusters.Remove(id);
        SaveClusters();
    }
}