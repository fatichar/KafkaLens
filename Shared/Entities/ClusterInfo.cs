using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using KafkaLens.Shared.Models;

namespace KafkaLens.Shared.Entities;

public class ClusterInfo(string id, string name, string address, string? protocol = null)
{
    [Required]
    [JsonPropertyName("id")]
    public string Id { get; set; } = id;

    [Required]
    [JsonPropertyName("name")]
    public string Name { get; set; } = name;

    [Required]
    [JsonPropertyName("address")]
    public string Address { get; set; } = address;

    [Required]
    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; } = protocol;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Credentials for this cluster, keyed by secret name (see <see cref="ClusterSecretNames"/>).
    /// Never serialised: <see cref="DataAccess.ClusterInfoRepository"/> keeps them in the OS credential store.
    /// Null means "not loaded / unchanged" so that an update without secrets keeps the stored ones.
    /// </summary>
    [JsonIgnore]
    public Dictionary<string, string>? Secrets { get; set; }

    /// <summary>Names of the secrets held in the credential store for this cluster (names only, no values).</summary>
    [JsonPropertyName("storedSecrets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? StoredSecretNames { get; set; }

    /// <summary>
    /// Plaintext secrets found in a configuration file (hand-written or from an older version). They are
    /// migrated to the credential store on load and removed from the file; they are only kept when no
    /// persistent credential store is available, so that nothing is lost. New secrets are never written here.
    /// </summary>
    [JsonPropertyName("secrets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string>? PlaintextSecrets { get; set; }

    /// <summary>
    /// Secrets listed in <see cref="StoredSecretNames"/> that could not be read at load time (e.g. the keyring was
    /// locked or unavailable). Their references are kept on unrelated updates so they resolve again later.
    /// </summary>
    [JsonIgnore]
    public HashSet<string>? UnresolvedSecretNames { get; set; }

    public string? GetSecret(string name) =>
        Secrets != null && Secrets.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value) ? value : null;

    public static ClusterInfo Create(string name, string address)
    {
        return new ClusterInfo(Guid.NewGuid().ToString(), name, address);
    }

    public static ClusterInfo Create(NewKafkaCluster newCluster)
    {
        return Create(newCluster.Name, newCluster.Address);
    }
}