
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Formatting;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels.Config;
using KafkaLens.ViewModels.Messages;
using CommunityToolkit.Mvvm.Messaging;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public IClusterInfoRepository ClusterInfoRepository { get; }
    public IClientInfoRepository ClientInfoRepository { get; }
    public IClientFactory ClientFactory { get; }

    // data
    public string? Title { get; private set; }

    public ObservableCollection<ClusterViewModel> Clusters { get; set; } = null!;

    public ObservableCollection<OpenedClusterViewModel> OpenedClusters { get; } = new();
    private readonly IDictionary<string, List<OpenedClusterViewModel>> openedClustersMap
        = new Dictionary<string, List<OpenedClusterViewModel>>();

    // services
    private readonly IClusterFactory clusterFactory;
    private readonly ISettingsService settingsService;
    private readonly ITopicSettingsService topicSettingsService;
    private readonly ISavedMessagesClient savedMessagesClient;
    private DispatcherTimer timer;

    // commands
    public IRelayCommand EditClustersCommand { get; }
    public IRelayCommand OpenClusterCommand { get; }
    public IRelayCommand OpenSavedMessagesCommand { get; set; }
    public IRelayCommand NextTabCommand { get; }
    public IRelayCommand PreviousTabCommand { get; }
    public IRelayCommand<string> SelectTabCommand { get; }

    public static Action ShowAboutDialog { get; set; } = () => { };
    public static Action ShowFolderOpenDialog { get; set; } = () => { };
    public static Action ShowEditClustersDialog { get; set; } = () => { };

    [ObservableProperty] private ObservableCollection<MenuItemViewModel>? menuItems;

    [ObservableProperty]
    private int selectedIndex = -1;

    [ObservableProperty]
    private string currentTheme;

    partial void OnCurrentThemeChanged(string value)
    {
        settingsService.SetValue("Theme", value);
        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(value));
    }

    partial void OnSelectedIndexChanging(int oldValue, int newValue)
    {
        if (oldValue >= 0 && oldValue < OpenedClusters.Count)
        {
            OpenedClusters[oldValue].IsCurrent = false;
        }
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (value >= 0 && value < OpenedClusters.Count)
        {
            OpenedClusters[value].IsCurrent = true;
        }
    }

    private readonly ObservableCollection<MenuItemViewModel> openClusterMenuItems = new();
    private MenuItemViewModel openMenu = null!;

    #region Init
    public MainViewModel(
        AppConfig appConfig,
        IClusterFactory clusterFactory,
        ISettingsService settingsService,
        ITopicSettingsService topicSettingsService,
        ISavedMessagesClient savedMessagesClient,
        IClusterInfoRepository clusterInfoRepository,
        IClientInfoRepository clientInfoRepository,
        IClientFactory clientFactory,
        FormatterFactory formatterFactory)
    {
        ClusterInfoRepository = clusterInfoRepository;
        ClientInfoRepository = clientInfoRepository;
        ClientFactory = clientFactory;
        Log.Information("Creating MainViewModel");
        this.clusterFactory = clusterFactory;
        this.settingsService = settingsService;
        this.topicSettingsService = topicSettingsService;
        this.savedMessagesClient = savedMessagesClient;
        OpenedClusterViewModel.FormatterFactory = formatterFactory;

        EditClustersCommand = new RelayCommand(EditClustersAsync);
        OpenClusterCommand = new RelayCommand<string>(OpenCluster);
        OpenSavedMessagesCommand = new RelayCommand(() => ShowFolderOpenDialog());
        NextTabCommand = new RelayCommand(NextTab);
        PreviousTabCommand = new RelayCommand(PreviousTab);
        SelectTabCommand = new RelayCommand<string>(s => SelectTab(int.Parse(s ?? "1")));

        Title = appConfig.Title;

        currentTheme = settingsService.GetValue("Theme") ?? "System";

        IsActive = true;

        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(appConfig.ClusterRefreshIntervalSeconds > 0 ? appConfig.ClusterRefreshIntervalSeconds : 60)
        };
        timer.Tick += (s, e) => _ = LoadClusters();
        timer.Start();

        if (Clusters is { Count: > 0 })
        {
            // OpenCluster(Clusters[0].Id);
        }
    }

    protected override async void OnActivated()
    {
        await LoadClusters();
    }

    public async Task LoadClusters()
    {
        if (Clusters == null)
        {
            Clusters = clusterFactory.GetAllClusters();
            Clusters.CollectionChanged += OnClustersChanged;

            openClusterMenuItems.Clear();
            foreach (var cluster in Clusters)
            {
                AddClusterToMenu(cluster);
            }
            CreateMenuItems();

            await clusterFactory.LoadClustersAsync();
        }
        else
        {
            await clusterFactory.LoadClustersAsync();
        }

        UpdateOpenedClusters();
    }

    private void UpdateOpenedClusters()
    {
        foreach (var openedCluster in OpenedClusters)
        {
            var cluster = Clusters.FirstOrDefault(c => c.Id == openedCluster.ClusterId);
            if (cluster != null)
            {
                openedCluster.Name = cluster.Name;
            }
        }
    }

    private void OnClustersChanged(object? sender, NotifyCollectionChangedEventArgs args)
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
            CreateViewMenu(),
            CreateHelpMenu()
        };
    }

    private MenuItemViewModel CreateViewMenu()
    {
        return new MenuItemViewModel
        {
            Header = "_View",
            Items = new()
            {
                new MenuItemViewModel
                {
                    Header = "_Theme",
                    Items = new()
                    {
                        new MenuItemViewModel { Header = "_Light", Command = new RelayCommand(() => CurrentTheme = "Light") },
                        new MenuItemViewModel { Header = "_Dark", Command = new RelayCommand(() => CurrentTheme = "Dark") },
                        new MenuItemViewModel { Header = "_System", Command = new RelayCommand(() => CurrentTheme = "System") }
                    }
                }
            }
        };
    }

    private MenuItemViewModel CreateClusterMenu()
    {
        openMenu = CreateOpenMenu();
        return new MenuItemViewModel
        {
            Header = "_Cluster",
            Items = new()
            {
                new MenuItemViewModel
                {
                    Header = "_Edit Clusters",
                    Command = EditClustersCommand,
                    Gesture = "Ctrl+E"
                },
                openMenu,
                new MenuItemViewModel
                {
                    Header = "_Open Saved Messages",
                    Command = OpenSavedMessagesCommand,
                    Gesture = "Ctrl+Shift+O"
                },
                new MenuItemViewModel()
                {
                    Header = "_Close Tab",
                    Command = new RelayCommand(CloseCurrentTab),
                    Gesture = "Ctrl+W"
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
        var statusIcon = new StatusIconViewModel { Color = c.StatusColor };
        c.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ClusterViewModel.StatusColor))
            {
                statusIcon.Color = c.StatusColor;
            }
        };

        return new MenuItemViewModel
        {
            Header = c.Name,
            Command = OpenClusterCommand,
            CommandParameter = c.Id,
            IsEnabled = true,
            Icon = statusIcon
        };
    }

    private MenuItemViewModel CreateOpenMenu()
    {
        return new MenuItemViewModel
        {
            Header = "_Open Cluster",
            Items = openClusterMenuItems
        };
    }

    private static MenuItemViewModel CreateHelpMenu()
    {
        return new MenuItemViewModel
        {
            Header = "_Help",
            Items = new()
            {
                new MenuItemViewModel
                {
                    Header = "_About",
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

        OpenCluster(cluster);
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

    private void NextTab()
    {
        if (OpenedClusters.Count > 1)
        {
            SelectedIndex = (SelectedIndex + 1) % OpenedClusters.Count;
        }
    }

    private void PreviousTab()
    {
        if (OpenedClusters.Count > 1)
        {
            SelectedIndex = (SelectedIndex - 1 + OpenedClusters.Count) % OpenedClusters.Count;
        }
    }

    private void SelectTab(int index)
    {
        if (index > 0 && index <= OpenedClusters.Count)
        {
            SelectedIndex = index - 1;
        }
    }

    internal void CloseTab(OpenedClusterViewModel openedCluster)
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

    internal void OpenCluster(ClusterViewModel clusterViewModel)
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

        var openedCluster = new OpenedClusterViewModel(settingsService, topicSettingsService, clusterViewModel, newName);
        alreadyOpened.Add(openedCluster);
        OpenedClusters.Add(openedCluster);
        _ = openedCluster.LoadTopicsAsync();
        SelectedIndex = OpenedClusters.Count - 1;
    }

    internal static string GenerateNewName(string clusterName, List<OpenedClusterViewModel> alreadyOpened)
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