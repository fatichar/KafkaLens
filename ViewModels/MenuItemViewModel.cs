using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KafkaLens.ViewModels;

public partial class MenuItemViewModel : ViewModelBase
{
    public string Header { get; set; } = "";
    public ICommand? Command { get; set; }
    public object? CommandParameter { get; set; }
    public ObservableCollection<MenuItemViewModel>? Items { get; set; }
    public bool IsEnabled { get; set; } = true;
    public object? Icon { get; set; }
    public MenuItemToggleType ToggleType { get; set; } = MenuItemToggleType.None;

    [ObservableProperty]
    private bool isChecked;
}