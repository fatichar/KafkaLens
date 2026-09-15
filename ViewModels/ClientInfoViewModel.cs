using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Clients.Entities;

namespace KafkaLens.ViewModels;

public partial class ClientInfoViewModel : ConnectionViewModelBase
{
    public ClientInfoViewModel(ClientInfo info)
    {
        Info = info;
        name = info.Name;
        address = info.Address;
        protocol = info.Protocol;
        isEnabled = info.IsEnabled;
        SetConnectionEnabled(isEnabled);
    }

    public ClientInfo Info { get; private set; }

    [ObservableProperty]
    private string name;
    
    [ObservableProperty]
    private string address;
    
    public string Id => Info.Id;
    
    [ObservableProperty]
    private string protocol;

    [ObservableProperty]
    private bool isEnabled;

    partial void OnIsEnabledChanged(bool value)
    {
        Info.IsEnabled = value;
        SetConnectionEnabled(value);
    }
    
    public void UpdateInfo(ClientInfo info)
    {
        Info = info;
        Name = info.Name;
        Address = info.Address;
        Protocol = info.Protocol;
        IsEnabled = info.IsEnabled;
    }
}
