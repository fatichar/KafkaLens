using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace KafkaLens.Shared.Entities;

public class ClusterConfig
{
    [JsonPropertyName("clusters")]
    public IList<ClusterInfo> Clusters { get; set; } = new List<ClusterInfo>();
}