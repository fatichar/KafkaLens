namespace KafkaLens.Shared.Models;

public class NewKafkaCluster(string name, string address, string? schemaRegistryUrl = null)
{
    public string Name { get; set; } = name;
    public string Address { get; set; } = address;
    public string? SchemaRegistryUrl { get; set; } = schemaRegistryUrl;
}