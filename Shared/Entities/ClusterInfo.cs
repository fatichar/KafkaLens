using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace KafkaLens.Shared.Entities;

public class ClusterInfo
{
    public ClusterInfo(string id, string name, string address, string? protocol = null)
    {
        Id = id;
        Name = name;
        Address = address;
        Protocol = protocol;
    }

    public ClusterInfo()
    {
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

    [Required]
    [JsonPropertyName("protocol")]
    public string? Protocol { get; set; }
}