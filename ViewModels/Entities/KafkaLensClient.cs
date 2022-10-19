using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace KafkaLens.ViewModels.Entities;

[Table("kafkalens_client")]
public class KafkaLensClient
{
    public KafkaLensClient(string id, string name, string serverUrl, string protocol)
    {
        Id = id;
        Name = name;
        this.ServerUrl = serverUrl;
        Protocol = protocol;
    }

    [Required]
    [Column("id")]
    public string Id { get; set; }
    
    [Required]
    [Column("name")]
    public string Name { get; set; }
    
    [Required]
    [Column("protocol")]
    public string Protocol { get; set; }
    
    [Required]
    [Column("server_url")]
    public string ServerUrl { get; set; }
}