using KafkaLens.App.Formating;
using System.Collections.ObjectModel;

namespace KafkaLens.App.ViewModels
{
    public interface IMessageSource
    {
        bool IsSelected { get; set; }
        ObservableCollection<MessageViewModel> Messages { get; }
        string Name { get; }
        IMessageFormatter Formatter { get; }
    }
}