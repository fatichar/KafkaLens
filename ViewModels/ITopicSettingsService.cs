namespace KafkaLens.ViewModels;

public interface ITopicSettingsService
{
    TopicSettings GetSettings(string clusterId, string topicName);
    void SetSettings(string clusterId, string topicName, TopicSettings settings, bool applyToAllClusters = false);
}
