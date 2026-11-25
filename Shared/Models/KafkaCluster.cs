using System.Collections.Generic;

namespace KafkaLens.Shared.Models;

public class KafkaCluster(string id, string name, string address, string? schemaRegistryUrl = null)
{
    public string Id { get; private set; } = id;
    public string Name { get; set; } = name;
    public string Address { get; set; } = address;
    public string? SchemaRegistryUrl { get; set; } = schemaRegistryUrl;
    public ConnectionState Status { get; set; } = ConnectionState.Unknown;
    public string? LastError { get; set; }
    public bool IsUnavailablePlaceholder { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>Security settings (no secrets). Null when the client does not expose them (e.g. gRPC).</summary>
    public ClusterSecurity? Security { get; set; }

    /// <summary>Names of the secrets stored for this cluster, so editors can show "saved" without the value.</summary>
    public IReadOnlyCollection<string> StoredSecretNames { get; set; } = [];
    public bool IsConnected => Status == ConnectionState.Connected;
}