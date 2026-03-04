using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;

namespace KafkaLens.ViewModels;

public partial class StatusIconViewModel : ViewModelBase
{
    [ObservableProperty]
    [System.Obsolete("Use Status instead")]
    private string color = "Gray";

    [ObservableProperty]
    private ConnectionState status = ConnectionState.Unknown;

    [ObservableProperty]
    private bool isLoading;

    partial void OnStatusChanged(ConnectionState value)
    {
        IsLoading = value == ConnectionState.Checking;
        Color = value switch
        {
            ConnectionState.Connected => "Green",
            ConnectionState.Failed => "Red",
            _ => "Gray"
        };
    }
}
