using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Clients.Entities;

namespace KafkaLens.ViewModels;

public partial class ClientInfoViewModel : ConnectionViewModelBase
{
    public ClientInfo Info { get; private set; }

    public ClientInfoViewModel(ClientInfo info)
    {
        Info = info;
        name = info.Name;
        address = info.Address;
        protocol = info.Protocol;
        apiKey = info.ApiKey;
    }

    [ObservableProperty]
    private string name;
    
    [ObservableProperty]
    private string address;

    [ObservableProperty]
    private string apiKey;
    
    public string Id => Info.Id;
    
    [ObservableProperty]
    private string protocol;
    
    public void UpdateInfo(ClientInfo info)
    {
        Info = info;
        Name = info.Name;
        Address = info.Address;
        Protocol = info.Protocol;
        ApiKey = info.ApiKey;
    }
}
