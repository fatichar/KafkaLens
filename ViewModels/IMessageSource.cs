using System.Collections.ObjectModel;

using KafkaLens.ViewModels.Formatting;

namespace KafkaLens.ViewModels
{
    public interface IMessageSource : ITreeNode
    {
        ObservableCollection<MessageViewModel> Messages { get; }
        IMessageFormatter Formatter { get; }
    }
}