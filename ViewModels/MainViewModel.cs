
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using HyperText.Avalonia.Models;
using KafkaLens.Shared;
using KafkaLens.Formatting;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels.Config;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public IClusterInfoRepository ClusterInfoRepository { get; }

    // data
    public string? Title { get; private set; }

    public ObservableCollection<ClusterViewModel> Clusters { get; set; }

    public ObservableCollection<OpenedClusterViewModel> OpenedClusters { get; } = new();
    private readonly IDictionary<string, List<OpenedClusterViewModel>> openedClustersMap
        = new Dictionary<string, List<OpenedClusterViewModel>>();

    // services
    private readonly IClusterFactory clusterFactory;
    private readonly ISettingsService settingsService;
    private readonly ISavedMessagesClient savedMessagesClient;

    // commands
    public IRelayCommand EditClustersCommand { get; }
    public IRelayCommand OpenClusterCommand { get; }
    public IRelayCommand OpenSavedMessagesCommand { get; set; }
    public static Action ShowAboutDialog { get; set; }
    public static Action ShowFolderOpenDialog { get; set; }
    public static Action ShowEditClustersDialog { get; set; }

    [ObservableProperty] private ObservableCollection<MenuItemViewModel>? menuItems;

    [ObservableProperty]
    private int selectedIndex = -1;

    partial void OnSelectedIndexChanging(int oldValue, int newValue)
    {
        if (oldValue >= 0)
        {
            OpenedClusters[oldValue].IsCurrent = false;
        }
    }
    
    partial void OnSelectedIndexChanged(int value)
    {
        OpenedClusters[value].IsCurrent = true;
    }

    private readonly ObservableCollection<MenuItemViewModel> openClusterMenuItems = new();
    private MenuItemViewModel openMenu;

    #region Init
    public MainViewModel(
        AppConfig appConfig,
        IClusterFactory clusterFactory,
        ISettingsService settingsService,
        ISavedMessagesClient savedMessagesClient,
        IClusterInfoRepository clusterInfoRepository,
        FormatterFactory formatterFactory)
    {
        ClusterInfoRepository = clusterInfoRepository;
        Log.Information("Creating MainViewModel");
        this.clusterFactory = clusterFactory;
        this.settingsService = settingsService;
        this.savedMessagesClient = savedMessagesClient;
        OpenedClusterViewModel.FormatterFactory = formatterFactory;

        EditClustersCommand = new RelayCommand(EditClustersAsync);
        OpenClusterCommand = new RelayCommand<string>(OpenCluster);
        OpenSavedMessagesCommand = new RelayCommand(() => ShowFolderOpenDialog());

        Title = appConfig?.Title ?? "";

        IsActive = true;

        if (Clusters.Count > 0)
        {
            OpenCluster(Clusters[0].Id);
        }
    }

    protected override async void OnActivated()
    {
        await LoadClusters();
        CreateMenuItems();
    }

    public async Task LoadClusters()
    {
        if (Clusters != null)
        {
            Clusters.CollectionChanged -= OnClustersChanged;
        }
        Clusters = await clusterFactory.LoadClustersAsync();
        openClusterMenuItems.Clear();
        foreach (var cluster in Clusters)
        {
            AddClusterToMenu(cluster);
        }

        Clusters.CollectionChanged += OnClustersChanged;
        CreateMenuItems();
    }

    private void OnClustersChanged(object sender, NotifyCollectionChangedEventArgs args)
    {
        if (args?.OldItems != null)
        {
            foreach (ClusterViewModel item in args.OldItems)
            {
                openClusterMenuItems.Remove(openClusterMenuItems.First(x => x.Header == item.Name));
            }
        }
        if (args?.NewItems != null)
        {
            foreach (ClusterViewModel item in args.NewItems)
            {
                AddClusterToMenu(item);
            }
        }
    }

    #endregion Init

    #region Menus
    private void CreateMenuItems()
    {
        MenuItems = new ObservableCollection<MenuItemViewModel>
        {
            CreateClusterMenu(),
            CreateHelpMenu()
        };
    }

    private MenuItemViewModel CreateClusterMenu()
    {
        openMenu = CreateOpenMenu();
        return new MenuItemViewModel
        {
            Header = "Cluster",
            Items = new()
            {
                new MenuItemViewModel
                {
                    Header = "Edit Clusters",
                    Command = EditClustersCommand,
                },
                openMenu,
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

    private void AddClusterToMenu(ClusterViewModel cluster)
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
            Items = new()
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

    private void EditClustersAsync()
    {
        ShowEditClustersDialog();
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
}