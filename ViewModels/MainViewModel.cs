
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using KafkaLens.Messages;
using KafkaLens.Shared;
using KafkaLens.ViewModels.DataAccess;
using KafkaLens.ViewModels.Entities;
using KafkaLens.Clients;
using KafkaLens.Formatting;
using KafkaLens.TaranaFormatters;
using Microsoft.EntityFrameworkCore;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel: ViewModelBase
{
    private const string HTTP_PROTOCOL_PREFIX = "http://";

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
    private readonly FormatterFactory formatterFactory;

    // commands
    public IRelayCommand AddClusterCommand { get; }
    public IRelayCommand OpenClusterCommand { get; }
    public IRelayCommand LoadClustersCommand { get; }
    
    [ObservableProperty]
    ObservableCollection<MenuItemViewModel> menuItems;

    [ObservableProperty]
    private int selectedIndex = -1;

    public MainViewModel(
        IOptions<AppConfig> appInfo,
        KafkaClientContext dbContext,
        ISettingsService settingsService,
        IKafkaLensClient localClient,
        FormatterFactory formatterFactory)
    {
        Log.Information("Creating MainViewModel");
        this.dbContext = dbContext;
        this.settingsService = settingsService;
        this.localClient = localClient;
        this.formatterFactory = formatterFactory;
        try
        {
            formatterFactory.AddFormatter(new GnmiFormatter());
            formatterFactory.AddFormatter(new EventFormatter());
        }
        catch (Exception e)
        {
            Console.WriteLine("Failed to add Gnmi formatter: " + e);
        }
        OpenedClusterViewModel.FormatterFactory = formatterFactory;

        AddClusterCommand = new RelayCommand(AddClusterAsync);
        // LoadClustersCommand = new RelayCommand(LoadClustersAsync);
        OpenClusterCommand = new RelayCommand<string>(OpenCluster);

        LoadClustersAsync().Wait();
        
        menuItems = CreateMenuItems();

        Title = $"Main - {appInfo?.Value?.Title}";

        IsActive = true;

        if (Clusters.Count > 0)
        {
            OpenCluster(Clusters[0].Id);
        }
    }

    private ObservableCollection<MenuItemViewModel> CreateMenuItems()
    {
        return new ObservableCollection<MenuItemViewModel>
        {
            CreateClusterMenu(),
            CreateHelpMenu()
        };
    }

    private static MenuItemViewModel CreateHelpMenu()
    {
        return new MenuItemViewModel
        {
            Header = "Help",
            Items = new []
            {
                new MenuItemViewModel
                {
                    Header = "About",
                    // Command = new RelayCommand(() => MessageBox.Show("About")),
                },
            }
        };
    }

    private MenuItemViewModel CreateClusterMenu()
    {
        return new MenuItemViewModel
        {
            Header = "Cluster",
            Items = new []
            {
                new MenuItemViewModel
                {
                    Header = "Add Cluster",
                    Command = AddClusterCommand,
                },
                CreateOpenMenu()
            }
        };
    }

    private MenuItemViewModel CreateOpenMenu()
    {
        var openClusterItems = Clusters.Select(c => new MenuItemViewModel
        {
            Header = c.Name,
            Command = OpenClusterCommand,
            CommandParameter = c.Id
        }).ToList();
        return new MenuItemViewModel
        {
            Header = "Load Clusters",
            Command = LoadClustersCommand,
            Items = new ObservableCollection<MenuItemViewModel>(openClusterItems)
        };
    }

    private void OpenCluster(string clusterId)
    {
        var cluster = Clusters.FirstOrDefault(c => c.Id == clusterId);
        if (cluster == null)
        {
            Log.Error("Failed to find cluster with id {ClusterId}", clusterId);
            return;
        }

        OpenCluster(cluster);
    }

    protected override void OnActivated()
    {
        Messenger.Register<MainViewModel, OpenClusterMessage>(this, (r, m) => r.Receive(m));
        Messenger.Register<MainViewModel, CloseTabMessage>(this, (r, m) => r.Receive(m));

        // LoadClustersCommand.Execute(null);
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

    private async Task LoadClustersAsync()
    {
        Clients.Clear();
        Clusters.Clear();
        await LoadClients();

        // call LoadClusters for each client in parallel
        var tasks = Clients.Select(LoadClusters).ToList();
        await Task.WhenAll(tasks);
    }

    private async Task LoadClusters(IKafkaLensClient client)
    {
        try
        {
            Log.Information("Loading clusters for client: {ClientName}", client.Name);
            var clusters = await client.GetAllClustersAsync();
            foreach (var cluster in clusters)
            {
                Log.Information("Found cluster: {ClusterName}", cluster.Name);
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
        foreach (var clientInfosKey in clientInfos.Values)
        {
            Log.Information("Found client: {ClientName} in database", clientInfosKey.Name);
        }
        foreach (var clientInfo in clientInfos.Values)
        {
            // Uncomment for local testing
            // break;
            //
            Log.Information("Loading client: {ClientName}", clientInfo.Name);
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
        if (!clientInfo.ServerUrl.StartsWith(HTTP_PROTOCOL_PREFIX))
        {
            clientInfo.ServerUrl = HTTP_PROTOCOL_PREFIX + clientInfo.ServerUrl;
        }
        switch (clientInfo.Protocol)
        {
            case "grpc":
                return new GrpcClient(clientInfo.ServerUrl);
            default:
                throw new ArgumentException($"Protocol {clientInfo.Protocol} is not supported");
        }
    }
}
