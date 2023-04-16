using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KafkaLens.ViewModels.Entities;

[Table("kafka_cluster")]
public class KafkaCluster
{
    public KafkaCluster(string id, string name, string address)
    {
        Id = id;
        Name = name;
        Address = address;
    }

    [Required]
    [Column("id")]
    public string Id { get; set; }

    [Column("client_id")]
    public string? ClientId { get; set; }

    [Required]
    [Column("name")]
    public string Name { get; set; }

    [Required]
    [Column("bootstrap_servers")]
    public string Address { get; set; }

    [ForeignKey("ClientId")]
    public KafkaLensClient? Client { get; set; }
}