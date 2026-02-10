using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared.Entities;

namespace KafkaLens.ViewModels;

public partial class ClusterInfoViewModel : ViewModelBase
{
    public ClusterInfo Info { get; }

    public ClusterInfoViewModel(ClusterInfo info)
    {
        Info = info;
    }

    public string Name => Info.Name;
    public string Address => Info.Address;
    public string Id => Info.Id;

    [ObservableProperty]
    private bool? isConnected;

    [ObservableProperty]
    private string connectionStatus = "Unknown";

    [ObservableProperty]
    private string statusColor = "Gray";

    partial void OnIsConnectedChanged(bool? value)
    {
        if (value == true)
        {
            ConnectionStatus = "Connected";
            StatusColor = "Green";
        }
        else if (value == false)
        {
            ConnectionStatus = "Disconnected";
            StatusColor = "Red";
        }
        else
        {
            ConnectionStatus = "Unknown";
            StatusColor = "Gray";
        }
    }
}
