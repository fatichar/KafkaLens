using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace KafkaLens.Clients.Entities;

public class KafkaLensClient
{
    public KafkaLensClient(string id, string name, string address, string protocol)
    {
        Id = id;
        Name = name;
        Address = address;
        Protocol = protocol;
    }

    [Required]
    [JsonPropertyName("id")]
    public string Id { get; set; }
    
    [Required]
    [JsonPropertyName("name")]
    public string Name { get; set; }
    
    [Required]
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; }
    
    [Required]
    [JsonPropertyName("address")]
    public string Address { get; set; }
}