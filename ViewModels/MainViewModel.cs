
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using KafkaLens.Core.Services;
using KafkaLens.Messages;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    // data
	public string? Title { get; }
    public ObservableCollection<ClusterViewModel> Clusters { get; } = new();
    public ObservableCollection<OpenedClusterViewModel> OpenedClusters { get; } = new();
    private readonly IDictionary<string, List<OpenedClusterViewModel>> openedClustersMap = new Dictionary<string, List<OpenedClusterViewModel>>();

    // services
    private readonly ISettingsService settingsService;
    private readonly IClusterService clusterService;

    // commands
    public IRelayCommand AddClusterCommand { get; }
    public IRelayCommand LoadClustersCommand { get; }

    public MainViewModel(IOptions<AppConfig> appInfo, ISettingsService settingsService, IClusterService clusterService)
    {
        this.settingsService = settingsService;
        this.clusterService = clusterService;

        AddClusterCommand = new RelayCommand(AddClusterAsync);
        LoadClustersCommand = new RelayCommand(LoadClustersAsync);

        Title = $"Main - {appInfo?.Value?.Title}";

        IsActive = true;


        LoadClustersCommand.Execute(null);
    }

    protected override void OnActivated()
    {
        Messenger.Register<MainViewModel, OpenClusterMessage>(this, (r, m) => r.Receive(m));
        Messenger.Register<MainViewModel, CloseTabMessage>(this, (r, m) => r.Receive(m));
    }

    private void AddClusterAsync()
    {
        // var openedCluster = new OpenedClusterViewModel(settingsService, clusterService, clusterViewModel, newName);
        // alreadyOpened.Add(openedCluster);
    }

    public void Receive(OpenClusterMessage message)
    {
        OpenCluster(message.ClusterViewModel);
        SelectedIndex = OpenedClusters.Count - 1;
    }

    private void Receive(CloseTabMessage message)
    {
        CloseTab(message.OpenedClusterViewModel);
    }

    private void CloseTab(OpenedClusterViewModel openedCluster)
    {
        Log.Information("Closing tab: {TabName}", openedCluster.Name);
        var openedList = openedClustersMap[openedCluster.ClusterId];
        openedList.Remove(openedCluster);
        if (openedList.Count == 0)
        {
            openedClustersMap.Remove(openedCluster.ClusterId);
        }
        OpenedClusters.Remove(openedCluster);
    }

    private void OpenCluster(ClusterViewModel clusterViewModel)
    {
        Log.Information("Opening cluster: {ClusterName}", clusterViewModel.Name);
        var newName = clusterViewModel.Name;
        openedClustersMap.TryGetValue(clusterViewModel.Id, out var alreadyOpened);
        if (alreadyOpened == null)
        {
            alreadyOpened = new List<OpenedClusterViewModel>();
            openedClustersMap.Add(clusterViewModel.Id, alreadyOpened);
        }
        else
        {
            // cluster already opened, so generate new name
            newName = GenerateNewName(clusterViewModel.Name, alreadyOpened);
        }
        var openedCluster = new OpenedClusterViewModel(settingsService, clusterService, clusterViewModel, newName);
        alreadyOpened.Add(openedCluster);
        OpenedClusters.Add(openedCluster);
        _ = openedCluster.LoadTopicsAsync();
    }

    private string GenerateNewName(string clusterName, List<OpenedClusterViewModel> alreadyOpened)
    {
        var existingNames = alreadyOpened.ConvertAll(c => c.Name);
        var suffixes = existingNames.ConvertAll(n => n.Length > clusterName.Length + 1 ? n.Substring(clusterName.Length + 1) : "");
        suffixes.Remove("");
        var numbersStrings = suffixes.ConvertAll(s => s.Length > 1 ? s.Substring(1, s.Length - 2) : "");
        var numbers = numbersStrings.ConvertAll(ns => int.TryParse(ns, out var number) ? number : 0);
        numbers.Sort();
        var smallestAvalable = numbers.Count + 1;
        for (var i = 0; i < numbers.Count; i++)
        {
            if (numbers[i] > i + 1)
            {
                smallestAvalable = i + 1;
                break;
            }
        }
        return $"{clusterName} ({smallestAvalable})";
    }

    private void LoadClustersAsync()
    {
        var clusters = clusterService.GetAllClusters();
        Clusters.Clear();
        foreach (var cluster in clusters)
        {
            Clusters.Add(new ClusterViewModel(cluster, clusterService));
        }
        selectedIndex = 0;

        if (Clusters.Count > 0)
        {
            OpenCluster(Clusters.First());
        }
    }

    private int selectedIndex = -1;

    public int SelectedIndex { get => selectedIndex; set => SetProperty(ref selectedIndex, value); }
}
