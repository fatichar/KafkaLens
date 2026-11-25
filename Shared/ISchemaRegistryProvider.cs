using KafkaLens.Shared.Models;

namespace KafkaLens.Shared;

/// <summary>Clients that know the full Schema Registry settings (URL, credentials, CA) of their clusters.</summary>
public interface ISchemaRegistryProvider
{
    /// <summary>The registry of a cluster, or null when none is configured.</summary>
    SchemaRegistrySettings? GetSchemaRegistry(string clusterId);
}
