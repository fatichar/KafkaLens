using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared.Entities;

namespace KafkaLens.ViewModels;

public partial class ClusterInfoViewModel : ConnectionViewModelBase
{
    public ClusterInfo Info { get; }

    public ClusterInfoViewModel(ClusterInfo info)
    {
        Info = info;
    }

    public string Name => Info.Name;
    public string Address => Info.Address;
    public string Id => Info.Id;
}
