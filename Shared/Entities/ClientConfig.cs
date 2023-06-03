using System.Collections.Generic;
using System.Text.Json.Serialization;
using KafkaLens.Clients.Entities;

namespace KafkaLens.Shared.Entities;

public class ClientConfig
{
    [JsonPropertyName("clients")]
    public IList<ClientInfo> Clients { get; set; }
}