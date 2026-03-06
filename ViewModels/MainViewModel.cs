using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Avalonia.Threading;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels.Config;
using KafkaLens.ViewModels.Messages;
using KafkaLens.ViewModels.Services;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    // Public dependencies exposed for external use
    public IClusterInfoRepository ClusterInfoRepository { get; }
    public IClientInfoRepository ClientInfoRepository { get; }
    public IClientFactory ClientFactory { get; }
    public IUpdateService UpdateService { get; }

    // Services
    internal readonly IMessageSaver messageSaver;
    internal readonly IFormatterService formatterService;
    internal readonly IClusterFactory clusterFactory;
    internal readonly ISettingsService settingsService;
    internal readonly ITopicSettingsService topicSettingsService;
    internal readonly ISavedMessagesClient savedMessagesClient;

    // Data
    public string? Title { get; private set; }
    public ObservableCollection<ClusterViewModel> Clusters { get; } = new();
    public ObservableCollection<OpenedClusterViewModel> OpenedClusters { get; } = new();

    // Commands
    public IRelayCommand EditClustersCommand { get; }
    public IRelayCommand OpenClusterCommand { get; }
    public IRelayCommand OpenSavedMessagesCommand { get; set; }
    public IRelayCommand NextTabCommand { get; }
    public IRelayCommand PreviousTabCommand { get; }
    public IRelayCommand<string> SelectTabCommand { get; }
    public IRelayCommand CloseCurrentTabCommand { get; }
    public IAsyncRelayCommand CheckForUpdatesCommand { get; }
    public IRelayCommand ShowPreferencesCommand { get; }

    // UI callbacks (set by the view layer)
    public static Action ShowAboutDialog { get; set; } = () => { };
    public static Action ShowFolderOpenDialog { get; set; } = () => { };
    public static Action ShowEditClustersDialog { get; set; } = () => { };
    public static Action<UpdateViewModel> ShowUpdateDialog { get; set; } = _ => { };
    public static Action<PreferencesViewModel> ShowPreferencesDialog { get; set; } = _ => { };
    public static Action<string, string> ShowMessage { get; set; } = (_, _) => { };
    public static Func<int, Task<bool>> ConfirmRestoreTabs { get; set; } = _ => Task.FromResult(false);

    [ObservableProperty] private ObservableCollection<MenuItemViewModel>? menuItems;
    [ObservableProperty] private int selectedIndex = -1;
    [ObservableProperty] private string currentTheme;
    [ObservableProperty] private bool autoCheckForUpdates;
    [ObservableProperty] private bool isLoadingClusters;

    private DispatcherTimer? timer;

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
            OpenedClusters[oldValue].IsCurrent = false;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        if (value >= 0 && value < OpenedClusters.Count)
            OpenedClusters[value].IsCurrent = true;
    }

    public MainViewModel(
        AppConfig appConfig,
        IClusterFactory clusterFactory,
        ISettingsService settingsService,
        ITopicSettingsService topicSettingsService,
        ISavedMessagesClient savedMessagesClient,
        IClusterInfoRepository clusterInfoRepository,
        IClientInfoRepository clientInfoRepository,
        IClientFactory clientFactory,
        IMessageSaver messageSaver,
        IFormatterService formatterService,
        IUpdateService updateService)
    {
        this.messageSaver = messageSaver;
        this.formatterService = formatterService;
        this.clusterFactory = clusterFactory;
        this.settingsService = settingsService;
        this.topicSettingsService = topicSettingsService;
        this.savedMessagesClient = savedMessagesClient;
        ClusterInfoRepository = clusterInfoRepository;
        ClientInfoRepository = clientInfoRepository;
        ClientFactory = clientFactory;
        UpdateService = updateService;

        Log.Information("Creating MainViewModel");

        EditClustersCommand = new RelayCommand(EditClustersAsync);
        OpenClusterCommand = new RelayCommand<string>(OpenCluster);
        OpenSavedMessagesCommand = new RelayCommand(() => ShowFolderOpenDialog());
        NextTabCommand = new RelayCommand(NextTab);
        PreviousTabCommand = new RelayCommand(PreviousTab);
        SelectTabCommand = new RelayCommand<string>(s => SelectTab(int.Parse(s ?? "1")));
        CloseCurrentTabCommand = new RelayCommand(CloseCurrentTab);
        CheckForUpdatesCommand = new AsyncRelayCommand(() => CheckForUpdatesAsync(false));
        ShowPreferencesCommand = new RelayCommand(ShowPreferences);

        OpenedClusters.CollectionChanged += (_, _) => UpdateCloseTabEnabled();
        Clusters.CollectionChanged += OnClustersChanged;

        CreateMenuItems();

        Title = appConfig.Title;
        currentTheme = settingsService.GetValue("Theme") ?? "System";
        autoCheckForUpdates =
            bool.TryParse(settingsService.GetValue("AutoCheckForUpdates") ?? "true", out var autoCheck) && autoCheck;

        IsActive = true;
        SetupPeriodicRefresh(appConfig);
    }

    private void SetupPeriodicRefresh(AppConfig appConfig)
    {
        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(appConfig.ClusterRefreshIntervalSeconds > 0
                ? appConfig.ClusterRefreshIntervalSeconds
                : 60)
        };
        timer.Tick += (_, _) => _ = RefreshClustersForHealthCheckAsync();
        timer.Start();
    }

    protected override async void OnActivated()
    {
        try
        {
            await LoadClustersOnStartupAsync();
            if (AutoCheckForUpdates)
                _ = CheckForUpdatesAsync(true);
        }
        catch (Exception e)
        {
            Log.Error(e, "Failed to initialize clusters on activation");
        }
    }

    // Compatibility wrapper used by tests and existing call sites.
    public Task LoadClusters() => LoadClustersOnStartupAsync();

    private void ShowPreferences() => ShowPreferencesDialog(new PreferencesViewModel(settingsService));

    private void EditClustersAsync() => ShowEditClustersDialog();
}
