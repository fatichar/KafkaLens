namespace KafkaLens.Shared.Models;

public class KafkaClusterUpdate
{
    public KafkaClusterUpdate(string name, string address)
    {
        Name = name;
        Address = address;
    }

    public string Name { get; set; }
    public string Address { get; set; }
}