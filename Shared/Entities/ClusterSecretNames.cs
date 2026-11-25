namespace KafkaLens.Shared.Entities;

/// <summary>Well-known names of per-cluster secrets.</summary>
public static class ClusterSecretNames
{
    public const string SaslPassword = "sasl.password";
    public const string SslKeyPassword = "ssl.key.password";
    public const string SchemaRegistryPassword = "schema.registry.password";
}
