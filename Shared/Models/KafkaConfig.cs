namespace KafkaLens.Shared.Models;

public class KafkaConfig
{
    public int QueryWatermarkTimeoutMs { get; set; } = 10000;
    public int QueryTopicsTimeoutMs { get; set; } = 5000;
    public int ConsumeTimeoutMs { get; set; } = 5000;
    public int AdminMetadataTimeoutMs { get; set; } = 3000;
    public int FetchMaxBytes { get; set; } = 2_000_000;
    public int StatisticsIntervalMs { get; set; } = 30_000;
    public bool EnableAutoOffsetStore { get; set; } = false;
    public bool EnableAutoCommit { get; set; } = false;
    public string GroupId { get; set; } = "KafkaLens.Server";
}
