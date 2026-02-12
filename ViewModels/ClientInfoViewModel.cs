using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Clients.Entities;

namespace KafkaLens.ViewModels;

public partial class ClientInfoViewModel : ConnectionViewModelBase
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
}
