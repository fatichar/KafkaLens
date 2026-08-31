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
    public bool IsConnected => Status == ConnectionState.Connected;
}