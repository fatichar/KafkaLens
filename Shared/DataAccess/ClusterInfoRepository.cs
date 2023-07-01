using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using KafkaLens.Shared.Entities;
using Serilog;

namespace KafkaLens.Shared.DataAccess;

public class ClusterInfoRepository : IClusterInfoRepository
{
    private const string DEFAULT_FILE_PATH = "cluster_info.json";
    private readonly string filePath;
    private Dictionary<string, ClusterInfo> clusters;
    public ReadOnlyDictionary<string, ClusterInfo> GetAll() => new(clusters);

    public ClusterInfoRepository(string filePath)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            Log.Warning("Clusters config file path not specified, using default: {DefaultPath}",
                DEFAULT_FILE_PATH);
            filePath = DEFAULT_FILE_PATH;
            File.CreateText(filePath);
        }
        this.filePath = filePath;

        LoadClusters();
    }

    private void LoadClusters()
    {
        if (!File.Exists(filePath))
        {
            Log.Error("File {FilePath} does not exist", filePath);
            File.CreateText(filePath);
        }

        var configFile = File.ReadAllText(filePath);
        var clusterConfig = JsonSerializer.Deserialize<ClusterConfig>(configFile);
        clusters = clusterConfig.Clusters.ToDictionary(cluster => cluster.Id);
    }

    public ClusterInfo GetById(string id)
    {
        ValidateClusterById(id);
        return clusters[id];
    }

    public ClusterInfo Add(string name, string address)
    {
        if (clusters.Values.Any(cluster => cluster.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new Exception($"Cluster with name \"{name}\" already exists. Names are not case sensitive.");
        }
        
        var clusterInfo = new ClusterInfo(
            Guid.NewGuid().ToString(),
            name,
            address
        );
        Add(clusterInfo);
        return clusterInfo;
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

    public void Add(ClusterInfo clusterInfo)
    {
        AddWithoutSaving(clusterInfo);
        SaveClusters();
    }

    private void AddWithoutSaving(ClusterInfo clusterInfo)
    {
        if (clusters.ContainsKey(clusterInfo.Id))
        {
            throw new Exception($"Cluster with id {clusterInfo.Id} already exists");
        }

        clusters.Add(clusterInfo.Id, clusterInfo);
    }

    public void AddAll(IEnumerable<ClusterInfo> clusterInfos)
    {
        foreach (var clusterInfo in clusterInfos)
        {
            AddWithoutSaving(clusterInfo);
        }
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

    public void Update(ClusterInfo clusterInfo)
    {
        ValidateClusterById(clusterInfo.Id);
        clusters[clusterInfo.Id] = clusterInfo;
        SaveClusters();
    }
    
    public void Delete(string id)
    {
        ValidateClusterById(id);
        clusters.Remove(id);
        SaveClusters();
    }

    public void DeleteAll()
    {
        clusters.Clear();
        SaveClusters();
    }
}