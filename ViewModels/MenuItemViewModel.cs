using System.Collections.ObjectModel;
using System.Windows.Input;

namespace KafkaLens.ViewModels;

public class MenuItemViewModel
{
    public string Header { get; set; }
    public ICommand Command { get; set; }
    public object CommandParameter { get; set; }
    public ObservableCollection<MenuItemViewModel> Items { get; set; }
    public bool IsEnabled { get; set; } = true;
    public object Icon { get; set; }
}