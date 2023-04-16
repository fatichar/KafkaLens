namespace KafkaLens.Shared.Models;

public class KafkaCluster
{
    public KafkaCluster(string id, string name, string address)
    {
        Id = id;
        Name = name;
        Address = address;
    }

    public string Id { get; set; }
    public string Name { get; set; }
    public string Address { get; set; }
    public bool IsConnected { get; set; } = true;
}