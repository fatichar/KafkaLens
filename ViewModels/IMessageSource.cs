using System.Collections.ObjectModel;

namespace KafkaLens.ViewModels;

public interface IMessageSource : ITreeNode
{
    ObservableCollection<MessageViewModel> Messages { get; }
    string? FormatterName { get; set; }
    string? KeyFormatterName { get; set; }
}