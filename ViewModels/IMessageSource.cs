using System.Collections.ObjectModel;
using KafkaLens.Formatting;

namespace KafkaLens.ViewModels;

public interface IMessageSource : ITreeNode
{
    ObservableCollection<MessageViewModel> Messages { get; }
    IMessageFormatter Formatter { get; }
}