using CommunityToolkit.Mvvm.ComponentModel;

namespace KafkaLens.ViewModels;

public partial class StatusIconViewModel : ViewModelBase
{
    [ObservableProperty]
    private string color;
}
