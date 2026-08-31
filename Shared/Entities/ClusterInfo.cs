using System;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using KafkaLens.Shared.Models;

namespace KafkaLens.Shared.Entities;

public class ClusterInfo(string id, string name, string address, string? protocol = null, string? schemaRegistryUrl = null)
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

    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; } = protocol;

    [JsonPropertyName("schemaRegistryUrl")]
    public string? SchemaRegistryUrl { get; set; } = schemaRegistryUrl;

    public static ClusterInfo Create(string name, string address, string? schemaRegistryUrl = null)
    {
        return new ClusterInfo(Guid.NewGuid().ToString(), name, address, schemaRegistryUrl: schemaRegistryUrl);
    }

    public static ClusterInfo Create(NewKafkaCluster newCluster)
    {
        return Create(newCluster.Name, newCluster.Address, newCluster.SchemaRegistryUrl);
    }
}