using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KafkaLens.Core.Entities;

[Table("cluster")]
public class KafkaCluster
{
    public KafkaCluster(string id, string name, string bootstrapServers)
    {
        Id = id;
        Name = name;
        BootstrapServers = bootstrapServers;
    }

    [Required]
    public string Id { get; set; }
    public string Name { get; set; }
    public string BootstrapServers { get; set; }
}