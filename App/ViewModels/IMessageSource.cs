using KafkaLens.App.Formating;
using System.Collections.ObjectModel;

namespace KafkaLens.App.ViewModels
{
    public interface IMessageSource : ITreeNode
    {
        ObservableCollection<MessageViewModel> Messages { get; }
        IMessageFormatter Formatter { get; }
    }
}