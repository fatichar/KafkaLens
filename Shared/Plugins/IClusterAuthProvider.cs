using System.Collections.Generic;

namespace KafkaLens.Shared.Plugins;

/// <summary>
/// Extension point: custom Kafka cluster authentication provider.
/// Implement and annotate with <c>[KafkaLensExtension(typeof(IClusterAuthProvider))]</c>.
/// </summary>
public interface IClusterAuthProvider
{
    /// <summary>Name shown in the UI auth-method picker.</summary>
    string Name { get; }

    /// <summary>
    /// Returns Confluent.Kafka configuration key-value pairs required
    /// to authenticate with the target cluster.
    /// </summary>
    IReadOnlyDictionary<string, string> GetConfig(string clusterId);
}
