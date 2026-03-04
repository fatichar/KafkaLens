namespace KafkaLens.Shared.Models;

public class KafkaCluster(string id, string name, string address)
{
    public string Id { get; private set; } = id;
    public string Name { get; set; } = name;
    public string Address { get; set; } = address;
    public ConnectionState Status { get; set; } = ConnectionState.Unknown;
    public string? LastError { get; set; }
    public bool IsConnected => Status == ConnectionState.Connected;
}