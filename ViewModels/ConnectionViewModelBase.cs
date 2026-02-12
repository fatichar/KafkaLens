using CommunityToolkit.Mvvm.ComponentModel;

namespace KafkaLens.ViewModels;

public partial class ConnectionViewModelBase : ViewModelBase
{
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
