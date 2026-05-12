using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels;

public partial class StatusIconViewModel : ViewModelBase
{
    [ObservableProperty]
    private string statusColor = "Gray";

    [ObservableProperty]
    private ConnectionState status = ConnectionState.Unknown;

    [ObservableProperty]
    private bool isLoading;

    partial void OnStatusChanged(ConnectionState value)
    {
        IsLoading = value == ConnectionState.Checking;
        StatusColor = value switch
        {
            ConnectionState.Connected => "Green",
            ConnectionState.Failed => "Red",
            _ => "Gray"
        };
    }
}