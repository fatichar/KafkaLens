
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using KafkaLens.Shared;
using KafkaLens.ViewModels.DataAccess;
using KafkaLens.ViewModels.Entities;
using KafkaLens.Clients;
using KafkaLens.Formatting;
using KafkaLens.Shared.Models;
using KafkaLens.TaranaFormatters;
using Microsoft.EntityFrameworkCore;
using Serilog;
using KafkaCluster = KafkaLens.Shared.Models.KafkaCluster;

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
    private readonly ISavedMessagesClient savedMessagesClient;

    // commands
    public IRelayCommand AddClusterCommand { get; }
    public IRelayCommand OpenClusterCommand { get; }
    public IRelayCommand OpenSavedMessagesCommand { get; set; }
    public static Action ShowAboutDialog { get; set; }
    public static Action ShowFolderOpenDialog { get; set; }

    [ObservableProperty]
    ObservableCollection<MenuItemViewModel> menuItems = new ObservableCollection<MenuItemViewModel>();

    [ObservableProperty]
    private int selectedIndex = -1;

    private ObservableCollection<MenuItemViewModel> openClusterMenuItems;

    #region Init
    public MainViewModel(
        IOptions<AppConfig> appInfo,
        KafkaClientContext dbContext,
        ISettingsService settingsService,
        IKafkaLensClient localClient,
        ISavedMessagesClient savedMessagesClient,
        FormatterFactory formatterFactory)
    {
        Log.Information("Creating MainViewModel");
        this.dbContext = dbContext;
        this.settingsService = settingsService;
        this.savedMessagesClient = savedMessagesClient;
        Clients.Add(localClient);
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
        OpenClusterCommand = new RelayCommand<string>(OpenCluster);
        OpenSavedMessagesCommand = new RelayCommand(() => ShowFolderOpenDialog());

        Title = $"Main - {appInfo?.Value?.Title}";

        IsActive = true;

        if (Clusters.Count > 0)
        {
            OpenCluster(Clusters[0].Id);
        }
    }

    protected override async void OnActivated()
    {
        CreateMenuItems();

        Clusters.CollectionChanged += (sender, args) =>
        {
            if (args.NewItems != null)
            {
                foreach (ClusterViewModel item in args.NewItems)
                {
                    UpdateMenuItems(item);
                }
            }
        };

        LoadClustersAsync();
    }
    #endregion Init

    #region Menus
    private void CreateMenuItems()
    {
        menuItems.Add(CreateClusterMenu());
        menuItems.Add(CreateHelpMenu());
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
                CreateOpenMenu(),
                new MenuItemViewModel
                {
                    Header = "Open Saved Messages",
                    Command = OpenSavedMessagesCommand,
                },
                new MenuItemViewModel()
                {
                    Header = "Close Tab",
                    Command = new RelayCommand(CloseCurrentTab),
                }
            }
        };
    }

    private void UpdateMenuItems(ClusterViewModel cluster)
    {
        openClusterMenuItems.Add(CreateOpenMenuItem(cluster));
    }

    private MenuItemViewModel CreateOpenMenuItem(ClusterViewModel c)
    {
        return new MenuItemViewModel
        {
            Header = c.Name,
            Command = OpenClusterCommand,
            CommandParameter = c.Id,
            IsEnabled = c.IsConnected
        };
    }

    private MenuItemViewModel CreateOpenMenu()
    {
        openClusterMenuItems = new ObservableCollection<MenuItemViewModel>();
        return new MenuItemViewModel
        {
            Header = "Open Cluster",
            Items = openClusterMenuItems
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
                    Command = new RelayCommand(() => ShowAboutDialog()),
                },
            }
        };
    }

    #endregion Menus

    private void OpenCluster(string? clusterId)
    {
        var cluster = Clusters.FirstOrDefault(c => c.Id == clusterId);
        if (cluster == null)
        {
            Log.Error("Failed to find cluster with id {ClusterId}", clusterId);
            return;
        }

        if (!cluster.IsConnected)
        {
            cluster.LoadTopicsCommand.Execute(null);
        }
        if (cluster.IsConnected)
        {
            OpenCluster(cluster);
        }
    }

    public async void OpenSavedMessages(string path)
    {
        var clusterName = Path.GetFileName(path) + "(saved)";
        var clusterViewModel = await AddOrGetCluster(clusterName, path);
        OpenCluster(clusterViewModel);
    }

    private async Task<ClusterViewModel> AddOrGetCluster(string clusterName, string path)
    {
        ClusterViewModel? existing = Clusters.FirstOrDefault(c => c.Name == clusterName);
        if (existing != null)
        {
            return existing;
        }
        NewKafkaCluster newCluster = new(clusterName, path);
        var cluster = await savedMessagesClient.AddAsync(newCluster);
        var clusterViewModel = new ClusterViewModel(cluster, savedMessagesClient);
        Clusters.Add(clusterViewModel);
        return clusterViewModel;
    }

    private void AddClusterAsync()
    {
        // var openedCluster = new OpenedClusterViewModel(settingsService, clusterService, clusterViewModel, newName);
        // alreadyOpened.Add(openedCluster);
    }

    private void CloseCurrentTab()
    {
        if (SelectedIndex >= 0)
        {
            var openedCluster = OpenedClusters[SelectedIndex];
            CloseTab(openedCluster);
        }
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
        SelectedIndex = OpenedClusters.Count - 1;
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
        var clientInfos = await dbContext.Clients.ToDictionaryAsync(client => client.Id);
        foreach (var clientInfosKey in clientInfos.Values)
        {
            Log.Information("Found client: {ClientName} in database", clientInfosKey.Name);
        }
        foreach (var clientInfo in clientInfos.Values)
        {
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
                return new GrpcClient(clientInfo.Name, clientInfo.ServerUrl);
            default:
                throw new ArgumentException($"Protocol {clientInfo.Protocol} is not supported");
        }
    }
}