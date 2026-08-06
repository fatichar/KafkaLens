namespace KafkaLens.ViewModels.Services;

public sealed record MessageSaveResult(string Directory, int Count);

public interface IMessageSaver
{
    Task<MessageSaveResult?> SaveAsync(IList<MessageViewModel> messages, string clusterName, bool formatted);
    bool CanSaveMessages(string clusterId);
}