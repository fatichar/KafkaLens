using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using KafkaLens.Shared.Entities;
using KafkaLens.Shared.Security;
using Serilog;

namespace KafkaLens.Shared.DataAccess;

public class ClusterInfoRepository : IClusterInfoRepository
{
    private const string DEFAULT_FILE_PATH = "cluster_info.json";
    private readonly string filePath;
    private readonly ISecretStore secretStore;
    private Dictionary<string, ClusterInfo> clusters = new();
    private ReadOnlyDictionary<string, ClusterInfo> readOnlyClusters;
    public ReadOnlyDictionary<string, ClusterInfo> GetAll() => readOnlyClusters;

    /// <param name="filePath">JSON file holding the (non-secret) cluster configuration.</param>
    /// <param name="secretStore">
    /// Where cluster credentials are kept. Defaults to a session-only in-memory store, which never persists secrets.
    /// </param>
    public ClusterInfoRepository(string filePath, ISecretStore? secretStore = null)
    {
        this.secretStore = secretStore ?? new InMemorySecretStore();
        readOnlyClusters = new(clusters);
        if (string.IsNullOrEmpty(filePath))
        {
            Log.Warning("Clusters config file path not specified, using default: {DefaultPath}",
                DEFAULT_FILE_PATH);
            filePath = DEFAULT_FILE_PATH;
        }
        this.filePath = filePath;

        LoadClusters();
    }

    private void LoadClusters()
    {
        if (!File.Exists(filePath))
        {
            Log.Error("File {FilePath} does not exist", filePath);
            SaveClusters();
        }

        var configFile = File.ReadAllText(filePath);
        var clusterConfig = JsonSerializer.Deserialize<ClusterConfig>(configFile);
        if (clusterConfig == null) return;
        var migrated = false;
        foreach (var cluster in clusterConfig.Clusters)
        {
            migrated |= LoadSecrets(cluster);
            clusters.Add(cluster.Id, cluster);
        }

        if (migrated)
        {
            SaveClusters();
        }
    }

    #region Secrets

    private string Key(ClusterInfo cluster, string name) => SecretKeys.ForCluster(cluster.Id, name);

    /// <summary>Resolves stored secrets and migrates plaintext ones. Returns true when the file must be rewritten.</summary>
    private bool LoadSecrets(ClusterInfo cluster)
    {
        cluster.Secrets = new Dictionary<string, string>();
        cluster.UnresolvedSecretNames = null;
        foreach (var name in cluster.StoredSecretNames ?? [])
        {
            try
            {
                var value = secretStore.Get(Key(cluster, name));
                if (value != null)
                    cluster.Secrets[name] = value;
                else
                    (cluster.UnresolvedSecretNames ??= new(StringComparer.Ordinal)).Add(name);
                if (value == null)
                    Log.Warning("Secret {SecretName} of cluster {ClusterName} was not found in {SecretStore}; it must be re-entered",
                        name, cluster.Name, secretStore.BackendName);
            }
            catch (Exception e)
            {
                (cluster.UnresolvedSecretNames ??= new(StringComparer.Ordinal)).Add(name);
                Log.Warning("Could not read secret {SecretName} of cluster {ClusterName} from {SecretStore}: {Error}",
                    name, cluster.Name, secretStore.BackendName, SecretRedactor.Redact(e.Message));
            }
        }

        if (cluster.PlaintextSecrets is not { Count: > 0 } plaintext)
        {
            cluster.PlaintextSecrets = null;
            return false;
        }

        foreach (var (name, value) in plaintext)
            cluster.Secrets[name] = value;

        if (!secretStore.IsPersistent)
        {
            // Keeping the file as it is: removing the values without a place to persist them would lose them.
            Log.Warning("Cluster {ClusterName} has {Count} plaintext secret(s) in {File}; they cannot be moved because no OS " +
                        "credential storage is available", cluster.Name, plaintext.Count, filePath);
            return false;
        }

        try
        {
            foreach (var (name, value) in plaintext)
                secretStore.Set(Key(cluster, name), value);
        }
        catch (Exception e)
        {
            Log.Warning("Could not migrate plaintext secrets of cluster {ClusterName} to {SecretStore}; leaving them in place: {Error}",
                cluster.Name, secretStore.BackendName, SecretRedactor.Redact(e.Message, plaintext.Values));
            return false;
        }

        cluster.StoredSecretNames = cluster.Secrets.Keys.Order(StringComparer.Ordinal).ToList();
        cluster.PlaintextSecrets = null;
        Log.Information("Moved {Count} plaintext secret(s) of cluster {ClusterName} to {SecretStore}",
            plaintext.Count, cluster.Name, secretStore.BackendName);
        return true;
    }

    /// <summary>Writes <see cref="ClusterInfo.Secrets"/> to the store and removes secrets that are no longer set.</summary>
    private void PersistSecrets(ClusterInfo cluster, ClusterInfo? previous)
    {
        if (cluster.Secrets == null)
        {
            // Caller did not touch secrets: keep what is stored.
            cluster.Secrets = previous?.Secrets ?? new Dictionary<string, string>();
            cluster.StoredSecretNames = previous?.StoredSecretNames;
            cluster.PlaintextSecrets = previous?.PlaintextSecrets;
            cluster.UnresolvedSecretNames = previous?.UnresolvedSecretNames;
            return;
        }

        var current = cluster.Secrets
            .Where(kv => !string.IsNullOrEmpty(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        foreach (var (name, value) in current)
            secretStore.Set(Key(cluster, name), value);

        // Secrets that could not be read at load time are not "removed" by the caller: keep their
        // references (and the stored values) unless a new value replaces them or the cluster's security
        // settings no longer use them.
        var applicable = (cluster.Security ?? new Models.ClusterSecurity()).ApplicableSecretNames();
        var unresolved = (cluster.UnresolvedSecretNames ?? []).Concat(previous?.UnresolvedSecretNames ?? [])
            .Where(n => !current.ContainsKey(n) && applicable.Contains(n))
            .ToHashSet(StringComparer.Ordinal);

        var previouslyStored = (previous?.StoredSecretNames ?? [])
            .Concat((IEnumerable<string>?)previous?.Secrets?.Keys ?? [])
            .Concat(cluster.StoredSecretNames ?? [])
            .Distinct()
            .ToList();
        foreach (var name in previouslyStored.Where(n => !current.ContainsKey(n) && !unresolved.Contains(n)))
            secretStore.Delete(Key(cluster, name));

        cluster.Secrets = current;
        var names = (secretStore.IsPersistent ? (IEnumerable<string>)current.Keys : []).Concat(unresolved).Distinct().Order(StringComparer.Ordinal).ToList();
        cluster.StoredSecretNames = names.Count > 0 ? names : null;
        cluster.UnresolvedSecretNames = unresolved.Count > 0 ? unresolved : null;

        // Unmigrated plaintext values (only possible without a persistent store) are stale once replaced or cleared.
        var plaintext = cluster.PlaintextSecrets ?? previous?.PlaintextSecrets;
        if (plaintext != null)
        {
            var remaining = plaintext.Where(kv => current.TryGetValue(kv.Key, out var v) && v == kv.Value)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            cluster.PlaintextSecrets = remaining.Count > 0 && !secretStore.IsPersistent ? remaining : null;
        }
    }

    private void DeleteSecrets(ClusterInfo cluster)
    {
        foreach (var name in (cluster.StoredSecretNames ?? []).Concat((IEnumerable<string>?)cluster.Secrets?.Keys ?? []).Distinct())
        {
            try
            {
                secretStore.Delete(Key(cluster, name));
            }
            catch (Exception e)
            {
                Log.Warning("Could not delete secret {SecretName} of cluster {ClusterName}: {Error}",
                    name, cluster.Name, SecretRedactor.Redact(e.Message));
            }
        }
    }

    #endregion Secrets

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

        var clusterInfo = ClusterInfo.Create(name, address);
        clusterInfo.Secrets = new Dictionary<string, string>();
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

        PersistSecrets(clusterInfo, null);
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

    // When the caller mutates the stored instance in place, compare against what was stored before.
    private static ClusterInfo Snapshot(ClusterInfo c) => new(c.Id, c.Name, c.Address, c.Protocol)
    {
        Secrets = c.Secrets,
        StoredSecretNames = c.StoredSecretNames?.ToList(),
        UnresolvedSecretNames = c.UnresolvedSecretNames,
        PlaintextSecrets = c.PlaintextSecrets
    };

    public void Update(ClusterInfo clusterInfo)
    {
        ValidateClusterById(clusterInfo.Id);
        var previous = clusters[clusterInfo.Id];
        PersistSecrets(clusterInfo, ReferenceEquals(previous, clusterInfo) ? Snapshot(previous) : previous);
        clusters[clusterInfo.Id] = clusterInfo;
        SaveClusters();
    }

    public void Delete(string id)
    {
        ValidateClusterById(id);
        DeleteSecrets(clusters[id]);
        clusters.Remove(id);
        SaveClusters();
    }

    public void DeleteAll()
    {
        foreach (var cluster in clusters.Values)
            DeleteSecrets(cluster);
        clusters.Clear();
        SaveClusters();
    }
}
