using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
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
    public IUpdateService UpdateService { get; }

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
    public IRelayCommand CloseCurrentTabCommand { get; }
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }

    public static Action ShowAboutDialog { get; set; } = () => { };
    public static Action ShowFolderOpenDialog { get; set; } = () => { };
    public static Action ShowEditClustersDialog { get; set; } = () => { };
    public static Action<UpdateViewModel> ShowUpdateDialog { get; set; } = (vm) => { };
    public static Action<string, string> ShowMessage { get; set; } = (title, message) => { };

    [ObservableProperty] private ObservableCollection<MenuItemViewModel>? menuItems;

    [ObservableProperty] private int selectedIndex = -1;

    [ObservableProperty] private string currentTheme;

    [ObservableProperty] private bool autoCheckForUpdates;

    partial void OnAutoCheckForUpdatesChanged(bool value)
    {
        settingsService.SetValue("AutoCheckForUpdates", value.ToString().ToLower());
        UpdateAutoCheckMenuCheckedState();
    }

    partial void OnCurrentThemeChanged(string value)
    {
        settingsService.SetValue("Theme", value);
        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(value));
        UpdateThemeMenuCheckedState();
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
    private MenuItemViewModel closeTabMenuItem = null!;

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
        IUpdateService updateService,
        FormatterFactory formatterFactory)
    {
        ClusterInfoRepository = clusterInfoRepository;
        ClientInfoRepository = clientInfoRepository;
        ClientFactory = clientFactory;
        UpdateService = updateService;
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
        CloseCurrentTabCommand = new RelayCommand(CloseCurrentTab);
        CheckForUpdatesCommand = new AsyncRelayCommand(() => CheckForUpdatesAsync(false));

        OpenedClusters.CollectionChanged += (_, _) => UpdateCloseTabEnabled();

        Title = appConfig.Title;

        currentTheme = settingsService.GetValue("Theme") ?? "System";
        autoCheckForUpdates =
            bool.TryParse(settingsService.GetValue("AutoCheckForUpdates") ?? "true", out var autoCheck) && autoCheck;

        IsActive = true;

        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(appConfig.ClusterRefreshIntervalSeconds > 0
                ? appConfig.ClusterRefreshIntervalSeconds
                : 60)
        };
        timer.Tick += (s, e) => _ = LoadClusters();
        timer.Start();
    }

    protected override async void OnActivated()
    {
        await LoadClusters();
        if (AutoCheckForUpdates)
        {
            _ = CheckForUpdatesAsync(true);
        }
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

#if DEBUG
            if (Clusters is { Count: > 0 })
            {
                var connected = Clusters.FirstOrDefault(c => c.IsConnected ?? false);
                if (connected != null)
                    OpenCluster(connected.Id);
            }
#endif
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

        // Ensure the initial theme state is reflected in the menu
        UpdateThemeMenuCheckedState();
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
                    Header = "Theme",
                    Items = CreateThemeMenuItems()
                }
            }
        };
    }

    private ObservableCollection<MenuItemViewModel> CreateThemeMenuItems()
    {
        var themes = new[] { "Light", "Bright", "Ocean", "Forest", "Purple", "Dark", "Gray", "System" };
        var items = new ObservableCollection<MenuItemViewModel>();

        foreach (var theme in themes)
        {
            items.Add(new MenuItemViewModel
            {
                Header = theme,
                Command = new RelayCommand(() => CurrentTheme = theme),
                ToggleType = MenuItemToggleType.Radio,
                IsChecked = theme == CurrentTheme
            });
        }

        return items;
    }

    private void UpdateMenuCheckedState()
    {
        UpdateThemeMenuCheckedState();
        UpdateAutoCheckMenuCheckedState();
    }

    private void UpdateThemeMenuCheckedState()
    {
        var viewMenu = MenuItems?.FirstOrDefault(m => m.Header == "_View");
        var themeMenu = viewMenu?.Items?.FirstOrDefault(m => m.Header == "Theme");

        if (themeMenu?.Items != null)
        {
            foreach (var item in themeMenu.Items)
            {
                item.IsChecked = item.Header == CurrentTheme;
            }
        }
    }

    private void UpdateAutoCheckMenuCheckedState()
    {
        var helpMenu = MenuItems?.FirstOrDefault(m => m.Header == "_Help");
        var autoCheckMenu = helpMenu?.Items?.FirstOrDefault(m => m.Header?.Contains("Auto-check") == true);
        if (autoCheckMenu != null)
        {
            autoCheckMenu.IsChecked = AutoCheckForUpdates;
        }
    }

    private MenuItemViewModel CreateClusterMenu()
    {
        openMenu = CreateOpenMenu();
        closeTabMenuItem = new MenuItemViewModel
        {
            Header = "_Close Tab",
            Command = CloseCurrentTabCommand,
            Gesture = KeyGesture.Parse("Ctrl+W"),
            IsEnabled = OpenedClusters.Count > 0
        };
        return new MenuItemViewModel
        {
            Header = "_Cluster",
            Items = new()
            {
                new MenuItemViewModel
                {
                    Header = "_Edit Clusters",
                    Command = EditClustersCommand,
                    Gesture = KeyGesture.Parse("Ctrl+E")
                },
                openMenu,
                new MenuItemViewModel
                {
                    Header = "_Open Saved Messages",
                    Command = OpenSavedMessagesCommand,
                    Gesture = KeyGesture.Parse("Ctrl+O")
                },
                closeTabMenuItem
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
        var menuItem = new MenuItemViewModel
        {
            Header = c.Name,
            Command = OpenClusterCommand,
            CommandParameter = c.Id,
            IsEnabled = true,
            Icon = statusIcon
        };

        c.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(ClusterViewModel.StatusColor))
            {
                statusIcon.Color = c.StatusColor;
            }
            else if (e.PropertyName == nameof(ClusterViewModel.Name))
            {
                menuItem.Header = c.Name;
            }
        };

        return menuItem;
    }

    private MenuItemViewModel CreateOpenMenu()
    {
        return new MenuItemViewModel
        {
            Header = "_Open Cluster",
            Items = openClusterMenuItems
        };
    }

    private MenuItemViewModel CreateHelpMenu()
    {
        return new MenuItemViewModel
        {
            Header = "_Help",
            Items = new()
            {
                new MenuItemViewModel
                {
                    Header = "Check for _Updates",
                    Command = CheckForUpdatesCommand,
                },
                new MenuItemViewModel
                {
                    Header = "Auto-check for Updates",
                    Command = new RelayCommand(() => AutoCheckForUpdates = !AutoCheckForUpdates),
                    ToggleType = MenuItemToggleType.CheckBox,
                    IsChecked = AutoCheckForUpdates
                },
                new MenuItemViewModel
                {
                    Header = "_About",
                    Command = new RelayCommand(() => ShowAboutDialog()),
                },
            }
        };
    }

    private void UpdateCloseTabEnabled()
    {
        if (closeTabMenuItem != null)
        {
            closeTabMenuItem.IsEnabled = OpenedClusters.Count > 0;
        }
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
        if (path.EndsWith('\\') || path.EndsWith('/'))
        {
            path = path.Remove(path.Length - 1, 1);
        };
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
        if (openedClustersMap.TryGetValue(openedCluster.ClusterId, out var openedList))
        {
            openedList.Remove(openedCluster);
            if (openedList.Count == 0)
            {
                openedClustersMap.Remove(openedCluster.ClusterId);
            }
        }

        OpenedClusters.Remove(openedCluster);
    }

    public async Task CheckForUpdatesAsync(bool silent)
    {
        Log.Information("Checking for updates...");
        var result = await UpdateService.CheckForUpdateAsync();
        if (result.UpdateAvailable)
        {
            if (!UpdateService.IsInstallDirectoryWritable())
            {
                if (!silent)
                {
                    ShowMessage("Update Available",
                        "A new update is available, but KafkaLens does not have permission to update itself in the " +
                        "current installation directory. Please move the application to a user-writable folder " +
                        "to enable auto-updates.");
                }

                return;
            }

            Log.Information("Update available: {Version}", result.LatestVersion);
            var updateVm = new UpdateViewModel(result);
            updateVm.OnUpdate += () => PerformUpdate(result);
            ShowUpdateDialog(updateVm);
        }
        else if (!silent)
        {
            Log.Information("No updates available.");
            // the space At the end is intentional. Without that the last word is not displayed.
            ShowMessage("Update Check", "You are already using the latest version of KafkaLens! ");
        }
    }

    private void PerformUpdate(UpdateCheckResult result)
    {
        if (!ValidateResult(result))
        {
            return;
        }
        if (!UpdateService.IsInstallDirectoryWritable())
        {
            Log.Warning("Install directory is not writable. Cannot perform auto-update.");
            return;
        }

        var appDir = AppContext.BaseDirectory;
        var updaterPath = Path.Combine(appDir, "KafkaLens.Updater");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) updaterPath += ".exe";

        Log.Information("Looking for updater at: {UpdaterPath}", updaterPath);
        Log.Information("App directory: {AppDir}", appDir);
        Log.Information("Current directory: {CurrentDir}", Directory.GetCurrentDirectory());

        if (!File.Exists(updaterPath))
        {
            Log.Error("Updater not found at {UpdaterPath}", updaterPath);

            // Try to find the updater in the current directory
            var currentDirUpdater = Path.Combine(Directory.GetCurrentDirectory(), "KafkaLens.Updater.exe");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(currentDirUpdater))
            {
                Log.Information("Found updater in current directory: {CurrentDirUpdater}", currentDirUpdater);
                updaterPath = currentDirUpdater;
            }
            else
            {
                Log.Error("Updater not found in current directory either: {CurrentDirUpdater}", currentDirUpdater);
                return;
            }
        }
        else
        {
            Log.Information("Updater found at: {UpdaterPath}", updaterPath);
        }

        var currentPid = Environment.ProcessId;
        var executablePath = Process.GetCurrentProcess().MainModule?.FileName!;

        var processStartInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            UseShellExecute = false
        };

        processStartInfo.ArgumentList.Add("--pid");
        processStartInfo.ArgumentList.Add(currentPid.ToString());
        processStartInfo.ArgumentList.Add("--url");
        processStartInfo.ArgumentList.Add(result.DownloadUrl!);
        if (result.ChecksumUrl != null)
        {
            processStartInfo.ArgumentList.Add("--checksum-url");
            processStartInfo.ArgumentList.Add(result.ChecksumUrl);
        }

        processStartInfo.ArgumentList.Add("--asset-name");
        processStartInfo.ArgumentList.Add(result.AssetName!);
        processStartInfo.ArgumentList.Add("--dest");
        processStartInfo.ArgumentList.Add(appDir);
        processStartInfo.ArgumentList.Add("--executable");
        processStartInfo.ArgumentList.Add(executablePath);

        Log.Information("Attempting to start updater process: {UpdaterPath}", updaterPath);
        Log.Information("Arguments: {Args}", string.Join(" ", processStartInfo.ArgumentList));

        try
        {
            var process = Process.Start(processStartInfo);
            Log.Information("Updater process started successfully with PID: {Pid}", process?.Id);

            if (process is { HasExited: false })
            {
                Log.Information("Updater process is running, exiting main application");
                // Exit the app
                Environment.Exit(0);
            }
            else
            {
                Log.Warning("Updater process exited immediately or failed to start");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start updater process");
            Log.Error("Exception details: {Message}", ex.Message);
            Log.Error("Stack trace: {StackTrace}", ex.StackTrace);
            throw;
        }
    }

    private bool ValidateResult(UpdateCheckResult result)
    {
        if (result.DownloadUrl == null)
        {
            Log.Error("Download URL is not set");
            return false;
        }
        if (result.AssetName == null)
        {
            Log.Error("Asset name is not set");
            return false;
        }

        return true;
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

        var openedCluster =
            new OpenedClusterViewModel(settingsService, topicSettingsService, clusterViewModel, newName);
        alreadyOpened.Add(openedCluster);
        OpenedClusters.Add(openedCluster);
        _ = openedCluster.LoadTopicsAsync();
        SelectedIndex = OpenedClusters.Count - 1;
    }

    internal static string GenerateNewName(string clusterName, List<OpenedClusterViewModel> alreadyOpened)
    {
        var existingNames = alreadyOpened.ConvertAll(c => c.Name);
        var suffixes = existingNames.ConvertAll(n =>
            n.Length > clusterName.Length + 1 ? n.Substring(clusterName.Length + 1) : "");
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