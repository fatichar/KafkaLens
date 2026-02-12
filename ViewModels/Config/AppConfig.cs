
namespace KafkaLens.ViewModels.Config;

public record AppConfig
{
	public string? Title { get; set; }
	public string ClusterInfoFilePath { get; set; } = "cluster_info.json";
	public string ClientInfoFilePath { get; set; } = "client_info.json";
    public int ClusterRefreshIntervalSeconds { get; set; } = 30;
}