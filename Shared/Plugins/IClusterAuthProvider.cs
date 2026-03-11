using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

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
    /// The method is async to support token refresh, secure-store lookups,
    /// or any other operation that may not complete synchronously.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> GetConfigAsync(
        string clusterId, CancellationToken ct = default);
}
