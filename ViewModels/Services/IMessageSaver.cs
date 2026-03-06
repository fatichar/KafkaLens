namespace KafkaLens.ViewModels.Services;

public interface IMessageSaver
{
    Task SaveAsync(IList<MessageViewModel> messages, string clusterName, bool formatted);
    bool CanSaveMessages(string clusterId);
}