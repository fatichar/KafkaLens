using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels;

public partial class ConnectionViewModelBase : ViewModelBase
{
    [ObservableProperty]
    private ConnectionState status = ConnectionState.Unknown;

    [ObservableProperty]
    private string? lastError;

    [ObservableProperty]
    private string connectionStatus = "Unknown";

    [ObservableProperty]
    private string statusColor = "Gray";

    [ObservableProperty]
    private bool isChecking;

    private bool connectionEnabled = true;

    internal void SetConnectionEnabled(bool enabled)
    {
        connectionEnabled = enabled;
        UpdateStatusPresentation(Status);
    }

    internal void SetDisabledStatus() => SetConnectionEnabled(false);

    internal void SetCheckingPresentation()
    {
        if (!connectionEnabled) return;
        IsChecking = true;
        ConnectionStatus = "Checking...";
    }

    partial void OnStatusChanged(ConnectionState value) => UpdateStatusPresentation(value);

    private void UpdateStatusPresentation(ConnectionState value)
    {
        if (!connectionEnabled)
        {
            IsChecking = false;
            ConnectionStatus = "Disabled";
            StatusColor = "Gray";
            return;
        }

        IsChecking = value == ConnectionState.Checking;
        switch (value)
        {
            case ConnectionState.Connected:
                ConnectionStatus = "Connected";
                StatusColor = "Green";
                break;
            case ConnectionState.Failed:
                ConnectionStatus = "Disconnected";
                StatusColor = "Red";
                break;
            case ConnectionState.Checking:
                ConnectionStatus = "Checking...";
                // Keep the color of the last state if it's not Unknown
                if (StatusColor == "Gray")
                {
                    StatusColor = "Gray";
                }
                break;
            case ConnectionState.Unknown:
            default:
                ConnectionStatus = "Unknown";
                StatusColor = "Gray";
                break;
        }
    }
}
