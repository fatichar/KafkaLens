namespace KafkaLens.Shared.Models;

/// <summary>Resolved Schema Registry connection for a cluster (including the password, never persisted).</summary>
public sealed record SchemaRegistrySettings(string Url, string? Username = null, string? Password = null, string? CaLocation = null)
{
    public override string ToString() => Url; // never print credentials
}
