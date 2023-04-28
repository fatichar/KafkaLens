
namespace KafkaLens.ViewModels.Config;

public record AppConfig
{
	public string Title { get; init; } = "KafkaLens";
	public string ClusterInfoFilePath { get; set; } = "";
	public string ClientInfoFilePath { get; set; } = "";
}
