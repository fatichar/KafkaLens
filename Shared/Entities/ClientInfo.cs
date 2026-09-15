using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace KafkaLens.Clients.Entities;

public class ClientInfo(string id, string name, string address, string protocol)
{
    [Required]
    [JsonPropertyName("id")]
    public string Id { get; set; } = id;

    [Required]
    [JsonPropertyName("name")]
    public string Name { get; set; } = name;

    [Required]
    [JsonPropertyName("protocol")]
    public string Protocol { get; set; } = protocol;

    [Required]
    [JsonPropertyName("address")]
    public string Address { get; set; } = address;

    [JsonPropertyName("isEnabled")]
    public bool IsEnabled { get; set; } = true;
}