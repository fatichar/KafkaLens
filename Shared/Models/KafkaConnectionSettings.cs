using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using KafkaLens.Shared.Entities;

namespace KafkaLens.Shared.Models;

/// <summary>Everything needed to open a Kafka connection: address, security settings and resolved secrets.</summary>
public sealed record KafkaConnectionSettings(
    string BootstrapServers,
    ClusterSecurity Security,
    IReadOnlyDictionary<string, string> Secrets)
{
    private static readonly IReadOnlyDictionary<string, string> NoSecrets = new Dictionary<string, string>();

    public static KafkaConnectionSettings Plain(string bootstrapServers) => new(bootstrapServers, new ClusterSecurity(), NoSecrets);

    public static KafkaConnectionSettings From(ClusterInfo cluster) =>
        new(cluster.Address, cluster.Security?.Normalized() ?? new ClusterSecurity(),
            cluster.Secrets ?? (IReadOnlyDictionary<string, string>)NoSecrets);

    public bool IsPlain => Security.IsDefault;

    public string? GetSecret(string name) => Secrets.TryGetValue(name, out var v) && !string.IsNullOrEmpty(v) ? v : null;

    /// <summary>
    /// Identifies the effective connection without revealing secrets (they are hashed), so cached
    /// consumers can be replaced when anything that affects the connection changes.
    /// </summary>
    public string Fingerprint()
    {
        var sb = new StringBuilder(BootstrapServers.Trim()).Append('|').Append(JsonSerializer.Serialize(Security.Normalized()));
        if (Secrets.Count > 0)
        {
            var secretText = string.Join("\n", Secrets.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => kv.Key + "=" + kv.Value));
            sb.Append('|').Append(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secretText))));
        }
        return sb.ToString();
    }

    public override string ToString() => $"{BootstrapServers} ({Security.Protocol})"; // never print secrets
}
