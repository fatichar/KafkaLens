using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace KafkaLens.Shared.Entities;

public class KafkaCluster
{
    public KafkaCluster(string id, string name, string address)
    {
        Id = id;
        Name = name;
        Address = address;
    }

    [Required]
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [Required]
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [Required]
    [JsonPropertyName("address")]
    public string Address { get; set; }
}