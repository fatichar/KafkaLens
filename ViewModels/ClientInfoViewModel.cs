using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Clients.Entities;

namespace KafkaLens.ViewModels;

public partial class ClientInfoViewModel : ViewModelBase
{
    public ClientInfo Info { get; }

    public ClientInfoViewModel(ClientInfo info)
    {
        Info = info;
    }

    public string Name => Info.Name;
    public string Address => Info.Address;
    public string Id => Info.Id;
    public string Protocol => Info.Protocol;

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
