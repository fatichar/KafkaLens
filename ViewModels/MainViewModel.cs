
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using KafkaLens.Messages;
using KafkaLens.Shared;
using KafkaLens.ViewModels.DataAccess;
using KafkaLens.ViewModels.Entities;
using KafkaLens.Clients;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Confluent.Kafka;

namespace KafkaLens.ViewModels;

public partial class MainViewModel : ObservableRecipient
{
    // data
    private string? Title { get; }

    public ObservableCollection<ClusterViewModel> Clusters { get; } = new();
    public ObservableCollection<IKafkaLensClient> Clients { get; } = new();

    public ObservableCollection<OpenedClusterViewModel> OpenedClusters { get; } = new();
    private readonly IDictionary<string, List<OpenedClusterViewModel>> openedClustersMap = new Dictionary<string, List<OpenedClusterViewModel>>();

    // services
    private readonly KafkaClientContext dbContext;
    private readonly ISettingsService settingsService;
    private readonly IKafkaLensClient localClient;

    // commands
    public IRelayCommand AddClusterCommand { get; }
    public IRelayCommand LoadClustersCommand { get; }

    private int selectedIndex = -1;

    public int SelectedIndex { get => selectedIndex; set => SetProperty(ref selectedIndex, value); }

    public MainViewModel(IOptions<AppConfig> appInfo, KafkaClientContext dbContext, ISettingsService settingsService, IKafkaLensClient localClient)
    {
        this.dbContext = dbContext;
        this.settingsService = settingsService;
        this.localClient = localClient;

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

    private void Receive(OpenClusterMessage message)
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

        var openedCluster = new OpenedClusterViewModel(settingsService, clusterViewModel, newName);
        alreadyOpened.Add(openedCluster);
        OpenedClusters.Add(openedCluster);
        _ = openedCluster.LoadTopicsAsync();
    }

    private static string GenerateNewName(string clusterName, List<OpenedClusterViewModel> alreadyOpened)
    {
        var existingNames = alreadyOpened.ConvertAll(c => c.Name);
        var suffixes = existingNames.ConvertAll(n => n.Length > clusterName.Length + 1 ? n.Substring(clusterName.Length + 1) : "");
        suffixes.Remove("");
        var numbersStrings = suffixes.ConvertAll(s => s.Length > 1 ? s.Substring(1, s.Length - 2) : "");
        var numbers = numbersStrings.ConvertAll(ns => int.TryParse(ns, out var number) ? number : 0);
        numbers.Sort();
        var smallestAvailable = numbers.Count + 1;
        for (var i = 0; i < numbers.Count; i++)
        {
            if (numbers[i] > i + 1)
            {
                smallestAvailable = i + 1;
                break;
            }
        }
        return $"{clusterName} ({smallestAvailable})";
    }

    private async void LoadClustersAsync()
    {
        Clients.Clear();
        Clusters.Clear();
        await LoadClients();

        foreach (var client in Clients)
        {
            await LoadClusters(client);
        }

        if (Clusters.Count > 0)
        {
            OpenCluster(Clusters.First());
            SelectedIndex = 0;
        }
    }

    private async Task LoadClusters(IKafkaLensClient client)
    {
        try
        {
            var clusters = await client.GetAllClustersAsync();
            foreach (var cluster in clusters)
            {
                Clusters.Add(new ClusterViewModel(cluster, client));
            }
        }
        catch (Exception e)
        {
            Log.Error(e, "Error loading clusters");
        }
    }

    private async Task LoadClients()
    {
        Clients.Add(localClient);
        var clientInfos = await dbContext.Clients.ToDictionaryAsync(client => client.Id);
        foreach (var clientInfo in clientInfos.Values)
        {
            try
            {
                var client = CreateClient(clientInfo);
                Clients.Add(client);
            }
            catch (Exception e)
            {
                Log.Error("Failed to load client {}", clientInfo.Name);
            }
        }
    }

    private static IKafkaLensClient CreateClient(KafkaLensClient clientInfo)
    {
        switch (clientInfo.Protocol)
        {
            case "grpc":
                return new GrpcClient(clientInfo.ServerUrl);
            default:
                throw new ArgumentException($"Protocol {clientInfo.Protocol} is not supported");
        }
    }
}
