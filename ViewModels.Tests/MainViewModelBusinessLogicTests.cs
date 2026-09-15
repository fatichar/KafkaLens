using System.Collections.ObjectModel;
using System.IO;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Services;
using KafkaLens.ViewModels.Config;
using KafkaLens.ViewModels.Messages;
using KafkaLens.ViewModels.Services;
using AvaloniaApp.Services;

namespace KafkaLens.ViewModels.Tests;

public class MainViewModelBusinessLogicTests
{
    private readonly IClusterFactory clusterFactory = Substitute.For<IClusterFactory>();
    private readonly ISettingsService settingsService = Substitute.For<ISettingsService>();
    private readonly ITopicSettingsService topicSettingsService = Substitute.For<ITopicSettingsService>();
    private readonly ISavedMessagesClient savedMessagesClient = Substitute.For<ISavedMessagesClient>();
    private readonly IClusterInfoRepository clusterInfoRepository = Substitute.For<IClusterInfoRepository>();
    private readonly IClientInfoRepository clientInfoRepository = Substitute.For<IClientInfoRepository>();
    private readonly IClientFactory clientFactory = Substitute.For<IClientFactory>();
    private readonly IMessageSaver messageSaver = Substitute.For<IMessageSaver>();
    private readonly IFormatterService formatterService = Substitute.For<IFormatterService>();
    private readonly IUpdateService updateService = Substitute.For<IUpdateService>();
    private readonly IAppLogService appLogService = new AppLogService();
    private readonly IKafkaLensClient mockClient = Substitute.For<IKafkaLensClient>();
    private readonly AppConfig appConfig = new() { Title = "Test", ClusterRefreshIntervalSeconds = 100 };
    private readonly PluginRegistry pluginRegistry;
    private readonly PluginRepositoryClient repoClient = new();
    private readonly PluginInstaller pluginInstaller;
    private readonly RepositoryManager repoManager;
    private readonly ExtensionRegistry extensionRegistry = new();
    private readonly IThemeService themeService;

    public MainViewModelBusinessLogicTests()
    {
        settingsService.GetBrowserConfig().Returns(new BrowserConfig());
        settingsService.GetValue(PreferencesViewModel.CONNECTION_CHECK_INTERVAL_SECONDS_KEY).Returns("0");
        settingsService.GetPluginSettings().Returns(new PluginSettings());
        clusterFactory.LoadClustersAsync().Returns(Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel>()));
        clusterFactory.LoadClustersForClientAsync(Arg.Any<IKafkaLensClient>()).Returns(Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel>()));
        clientFactory.GetAllClients().Returns(new List<IKafkaLensClient>());
        MainViewModel.ConfirmRestoreTabs = (count) => Task.FromResult(true);

        pluginRegistry  = new PluginRegistry(Path.GetTempPath(), settingsService, extensionRegistry);
        pluginInstaller = new PluginInstaller(Path.GetTempPath(), settingsService);
        repoManager     = new RepositoryManager(settingsService);
        themeService    = new ThemeService(extensionRegistry, pluginsDir: Path.GetTempPath());
    }

    private MainViewModel CreateViewModel(ObservableCollection<ClusterViewModel>? clusters = null)
    {
        if (clusters != null)
        {
            clusterFactory.LoadClustersAsync().Returns(Task.FromResult<IReadOnlyList<ClusterViewModel>>(clusters));
            // For per-client loading, we need to mock the clients and the per-client load
            var clients = clusters.Select(c => c.Client).Distinct().ToList();
            if (clients.Count > 0)
            {
                clientFactory.GetAllClients().Returns(clients);
                foreach (var client in clients)
                {
                    var clientClusters = clusters.Where(c => c.Client == client).ToList();
                    clusterFactory.LoadClustersForClientAsync(client).Returns(Task.FromResult<IReadOnlyList<ClusterViewModel>>(clientClusters));
                }
            }
        }

        return new MainViewModel(
            appConfig,
            clusterFactory,
            settingsService,
            topicSettingsService,
            savedMessagesClient,
            clusterInfoRepository,
            clientInfoRepository,
            clientFactory,
            messageSaver,
            formatterService,
            updateService,
            pluginRegistry,
            repoClient,
            pluginInstaller,
            repoManager,
            themeService,
            appLogService);
    }

    private (ClusterViewModel vm, KafkaCluster model) CreateClusterVmWithModel(string id = "c1", string name = "Cluster1", string address = "localhost:9092")
    {
        var cluster = new KafkaCluster(id, name, address);
        return (new ClusterViewModel(cluster, mockClient), cluster);
    }

    private ClusterViewModel CreateClusterVm(string id = "c1", string name = "Cluster1", string address = "localhost:9092")
    {
        return CreateClusterVmWithModel(id, name, address).vm;
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task UnavailableCluster_IsNotRestoredOpenedListedOrChecked(bool enabled, bool ownerEnabled)
    {
        mockClient.Name.Returns("Remote");
        var cluster = CreateClusterVm();
        cluster.IsEnabled = enabled;
        clientInfoRepository.GetAll().Returns(new ReadOnlyDictionary<string, KafkaLens.Clients.Entities.ClientInfo>(
            new Dictionary<string, KafkaLens.Clients.Entities.ClientInfo>
            {
                ["remote"] = new("remote", "Remote", "http://remote", "grpc") { IsEnabled = ownerEnabled }
            }));
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            RestoreTabsOnStartup = true,
            OpenedTabs = new List<OpenedTabState> { new() { ClusterId = cluster.Id } }
        });
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster });
        await vm.LoadClusters();
        if (!vm.Clusters.Contains(cluster)) vm.Clusters.Add(cluster);
        vm.OpenCluster(cluster);
        await vm.RefreshClustersForHealthCheckAsync();
        Assert.Empty(vm.OpenedClusters);
        Assert.Empty(vm.MenuItems![0].Items![1].Items!);
        Assert.False(cluster.IsAvailable);
        Assert.Equal("Disabled", cluster.ConnectionStatus);
        Assert.Equal("Gray", cluster.StatusColor);
        await mockClient.DidNotReceive().ValidateConnectionAsync(Arg.Any<string>());
        await mockClient.DidNotReceive().GetTopicsAsync(Arg.Any<string>());
        if (!ownerEnabled) await clusterFactory.DidNotReceive().LoadClustersForClientAsync(mockClient);
    }

    [AvaloniaFact]
    public async Task PeriodicCycle_StartsSiblingWhileFirstCheckIsPending_AndSkipsOverlappingCycle()
    {
        mockClient.Name.Returns("Local");
        mockClient.CanEditClusters.Returns(true);
        mockClient.ValidateConnectionAsync(Arg.Any<string>()).Returns(true);
        var first = CreateClusterVm("first", "First", "first:9092");
        var second = CreateClusterVm("second", "Second", "second:9092");
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { first, second });
        await vm.LoadClusters();
        mockClient.ClearReceivedCalls();
        var pending = new TaskCompletionSource<bool>();
        mockClient.ValidateConnectionAsync(first.Address).Returns(pending.Task);
        var secondStarted = new TaskCompletionSource();
        mockClient.ValidateConnectionAsync(second.Address).Returns(_ =>
        {
            secondStarted.TrySetResult();
            return Task.FromResult(true);
        });
        var cycle = vm.RefreshClustersForHealthCheckAsync();
        await secondStarted.Task;
        await vm.RefreshClustersForHealthCheckAsync();
        await mockClient.Received(1).ValidateConnectionAsync(second.Address);
        Assert.False(cycle.IsCompleted);
        pending.SetResult(false);
        await cycle;
        Assert.Equal(ConnectionState.Failed, first.Status);
        Assert.Equal(ConnectionState.Connected, second.Status);
    }

    [AvaloniaFact]
    public async Task GenuineDeletionWithOneSibling_ClosesTabInsteadOfReattaching()
    {
        mockClient.ValidateConnectionAsync(Arg.Any<string>()).Returns(true);
        var first = CreateClusterVm("first");
        var second = CreateClusterVm("second");
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { first, second });
        await vm.LoadClusters();
        vm.OpenCluster(first);
        clusterFactory.LoadClustersForClientAsync(mockClient).Returns(
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new[] { second }));
        await vm.LoadClusters();
        Assert.Empty(vm.OpenedClusters);
        Assert.Same(second, Assert.Single(vm.Clusters));
    }

    [AvaloniaFact]
    public async Task ReconciledMenu_DetachesDiscardedItems_AndResetDetachesAllItems()
    {
        mockClient.ValidateConnectionAsync(Arg.Any<string>()).Returns(true);
        var cluster = CreateClusterVm();
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster });
        await vm.LoadClusters();
        var discarded = new List<MenuItemViewModel>();
        for (var i = 0; i < 20; i++)
        {
            discarded.Add(Assert.Single(vm.MenuItems![0].Items![1].Items!));
            vm.RefreshAvailability();
        }
        cluster.Name = "Renamed";
        cluster.Status = ConnectionState.Failed;
        var current = Assert.Single(vm.MenuItems![0].Items![1].Items!);
        Assert.Equal("Renamed", current.Header);
        Assert.Equal(ConnectionState.Failed, ((StatusIconViewModel)current.Icon!).Status);
        Assert.All(discarded, item => Assert.NotEqual("Renamed", item.Header));
        vm.Clusters.Clear();
        cluster.Name = "After removal";
        Assert.Equal("Renamed", current.Header);
        Assert.Empty(vm.MenuItems![0].Items![1].Items!);
    }

    [AvaloniaFact]
    public async Task DisabledConfiguredLocalName_DoesNotDisableBuiltInDirectClusters()
    {
        mockClient.Name.Returns("Local");
        mockClient.CanEditClusters.Returns(true);
        mockClient.ValidateConnectionAsync(Arg.Any<string>()).Returns(true);
        clientInfoRepository.GetAll().Returns(new ReadOnlyDictionary<string, KafkaLens.Clients.Entities.ClientInfo>(
            new Dictionary<string, KafkaLens.Clients.Entities.ClientInfo>
            {
                ["unrelated"] = new("unrelated", "Local", "http://remote", "grpc") { IsEnabled = false }
            }));
        var cluster = CreateClusterVm();
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster });
        await vm.LoadClusters();
        mockClient.ClearReceivedCalls();
        await vm.RefreshClustersForHealthCheckAsync();
        Assert.True(cluster.IsAvailable);
        await mockClient.Received(1).ValidateConnectionAsync(cluster.Address);
        Assert.Single(vm.MenuItems![0].Items![1].Items!);
    }

    [Fact]
    public async Task ClientFactory_ReloadReusesUnchangedInstances_AndExcludesDisabledClients()
    {
        mockClient.Name.Returns("Local");
        var remote = new KafkaLens.Clients.Entities.ClientInfo("remote", "Remote", "http://remote", "grpc");
        clientInfoRepository.GetAll().Returns(new ReadOnlyDictionary<string, KafkaLens.Clients.Entities.ClientInfo>(
            new Dictionary<string, KafkaLens.Clients.Entities.ClientInfo> { [remote.Id] = remote }));
        var factory = new ClientFactory(clientInfoRepository, mockClient);
        await factory.LoadClientsAsync();
        var instance = factory.GetClient("Remote");
        await factory.LoadClientsAsync();
        Assert.Same(instance, factory.GetClient("Remote"));
        remote.IsEnabled = false;
        await factory.LoadClientsAsync();
        Assert.Same(mockClient, Assert.Single(factory.GetAllClients()));
        Assert.Same(instance, factory.GetClient("Remote"));
        ((IDisposable)instance).Dispose();
    }

    [AvaloniaFact]
    public async Task LoadClusters_FirstCall_ShouldInitializeClustersAndCreateMenuItems()
    {
        // Arrange
        var cluster1 = CreateClusterVm("c1", "Cluster1");
        var cluster2 = CreateClusterVm("c2", "Cluster2");
        var clusters = new ObservableCollection<ClusterViewModel> { cluster1, cluster2 };
        var vm = CreateViewModel(clusters);

        // Act
        await vm.LoadClusters();

        // Assert
        Assert.NotNull(vm.Clusters);
        Assert.Equal(2, vm.Clusters.Count);
        Assert.NotNull(vm.MenuItems);
        // OnActivated() in constructor also calls LoadClusters, so at least 1 call expected
        await clusterFactory.Received().LoadClustersForClientAsync(Arg.Any<IKafkaLensClient>());
    }

    [AvaloniaFact]
    public async Task LoadClusters_SecondCall_ShouldNotReinitializeClusters()
    {
        // Arrange
        var clusters = new ObservableCollection<ClusterViewModel> { CreateClusterVm() };
        var vm = CreateViewModel(clusters);
        await vm.LoadClusters();

        // Act
        await vm.LoadClusters();

        // Assert — GetAllClusters called once (first call only), LoadClustersAsync called multiple times
        // OnActivated() in constructor also calls LoadClusters, so total is 3+
        await clusterFactory.Received().LoadClustersForClientAsync(Arg.Any<IKafkaLensClient>());
    }

    [AvaloniaFact]
    public async Task LoadClusters_ShouldUpdateOpenedClusterNames()
    {
        // Arrange
        var (cluster, model) = CreateClusterVmWithModel("c1", "OriginalName");
        var clusters = new ObservableCollection<ClusterViewModel> { cluster };
        var vm = CreateViewModel(clusters);
        await vm.LoadClusters();

        // Open the cluster
        vm.OpenCluster(cluster);
        Assert.Single(vm.OpenedClusters);
        Assert.Equal("OriginalName", vm.OpenedClusters[0].Name);

        // Reconfigure mock to return a cluster with same ID but different name
        var updatedCluster = CreateClusterVm("c1", "RenamedCluster");
        clusterFactory.LoadClustersForClientAsync(mockClient).Returns(
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel> { updatedCluster }));

        // Act — second LoadClusters should update opened cluster names
        await vm.LoadClusters();

        // Assert
        Assert.Equal("RenamedCluster", vm.OpenedClusters[0].Name);
    }

    [AvaloniaFact]
    public async Task LoadClusters_WhenExistingClusterHasKnownStatus_ShouldIgnoreUnknownSnapshotStatus()
    {
        // Arrange
        var cluster = CreateClusterVm("c1", "Cluster1");
        cluster.Status = ConnectionState.Connected;
        mockClient.ValidateConnectionAsync(cluster.Address).Returns(Task.FromResult(true));

        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster });
        await vm.LoadClusters();

        var unknownSnapshotCluster = CreateClusterVm("c1", "Cluster1");
        unknownSnapshotCluster.Status = ConnectionState.Unknown;
        clusterFactory.LoadClustersForClientAsync(mockClient).Returns(
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel> { unknownSnapshotCluster }));

        // Act
        await vm.LoadClusters();

        // Assert
        Assert.Single(vm.Clusters);
        Assert.Equal(ConnectionState.Connected, vm.Clusters[0].Status);
        Assert.Equal("Green", vm.Clusters[0].StatusColor);
    }

    [AvaloniaFact]
    public async Task OpenCluster_ShouldAddToOpenedClusters()
    {
        // Arrange
        var cluster = CreateClusterVm();
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster });
        await vm.LoadClusters();

        // Act
        vm.OpenCluster(cluster);

        // Assert
        Assert.Single(vm.OpenedClusters);
        Assert.Equal(cluster.Name, vm.OpenedClusters[0].Name);
        Assert.Equal(0, vm.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task OpenCluster_ShouldLogUserFacingEvent()
    {
        // Arrange
        var cluster = CreateClusterVm();
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster });
        await vm.LoadClusters();

        // Act
        vm.OpenCluster(cluster);

        // Assert
        Assert.Contains(appLogService.Entries, e => e.Message == "Opening cluster Cluster1");
    }

    // [AvaloniaFact]
    public async Task OpenCluster_SameClusterTwice_ShouldGenerateNewName()
    {
        // Arrange
        var cluster = CreateClusterVm("c1", "MyCluster");
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster });
        await vm.LoadClusters();

        // Act
        vm.OpenCluster(cluster);
        vm.OpenCluster(cluster);

        // Assert
        Assert.Equal(2, vm.OpenedClusters.Count);
        Assert.Equal("MyCluster", vm.OpenedClusters[0].Name);
        Assert.Equal("MyCluster (1)", vm.OpenedClusters[1].Name);
        Assert.Equal(1, vm.SelectedIndex);
    }

    [AvaloniaFact]
    public async Task OpenCluster_MultipleDifferentClusters_ShouldKeepOriginalNames()
    {
        // Arrange
        var cluster1 = CreateClusterVm("c1", "Cluster1");
        var cluster2 = CreateClusterVm("c2", "Cluster2");
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster1, cluster2 });
        await vm.LoadClusters();

        // Act
        vm.OpenCluster(cluster1);
        vm.OpenCluster(cluster2);

        // Assert
        Assert.Equal(2, vm.OpenedClusters.Count);
        Assert.Equal("Cluster1", vm.OpenedClusters[0].Name);
        Assert.Equal("Cluster2", vm.OpenedClusters[1].Name);
    }

    [AvaloniaFact]
    public async Task CloseTab_ShouldRemoveFromOpenedClusters()
    {
        // Arrange
        var cluster = CreateClusterVm();
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster });
        await vm.LoadClusters();
        vm.OpenCluster(cluster);
        var openedCluster = vm.OpenedClusters[0];

        // Act
        vm.CloseTab(openedCluster);

        // Assert
        Assert.Empty(vm.OpenedClusters);
    }

    // [AvaloniaFact]
    public async Task CloseTab_WithMultipleOpenedSameCluster_ShouldOnlyRemoveOne()
    {
        // Arrange
        var cluster = CreateClusterVm("c1", "MyCluster");
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster });
        await vm.LoadClusters();
        vm.OpenCluster(cluster);
        vm.OpenCluster(cluster);
        Assert.Equal(2, vm.OpenedClusters.Count);

        // Act — close the first tab
        vm.CloseTab(vm.OpenedClusters[0]);

        // Assert
        Assert.Single(vm.OpenedClusters);
    }

    [AvaloniaFact]
    public async Task OnClustersChanged_AddingCluster_ShouldUpdateMenuItems()
    {
        // Arrange
        var newCluster = CreateClusterVm("c2", "NewCluster");
        clientFactory.GetAllClients().Returns(new List<IKafkaLensClient> { mockClient });
        clusterFactory.LoadClustersForClientAsync(mockClient).Returns(
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel>()),
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel> { newCluster }));
        var vm = CreateViewModel();

        // Act - first load gets empty snapshot
        await vm.LoadClusters();
        Assert.NotNull(vm.MenuItems);
        await vm.LoadClusters();

        // Assert - menu items updated after second snapshot
        Assert.Single(vm.Clusters);
    }

    [AvaloniaFact]
    public async Task OnClustersChanged_RemovingCluster_ShouldUpdateMenuItems()
    {
        // Arrange
        var cluster = CreateClusterVm("c1", "Cluster1");
        clientFactory.GetAllClients().Returns(new List<IKafkaLensClient> { mockClient });
        clusterFactory.LoadClustersForClientAsync(mockClient).Returns(
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel> { cluster }),
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel>()));
        var vm = CreateViewModel();

        // Act - first load gets one cluster
        await vm.LoadClusters();
        await vm.LoadClusters();

        // Assert
        Assert.Empty(vm.Clusters);
    }

    [AvaloniaFact]
    public async Task RefreshReplacingPlaceholder_ShouldReattachOpenTab()
    {
        mockClient.Name.Returns("Remote");
        var placeholder = new ClusterViewModel(new KafkaCluster("placeholder", "Remote", "remote:9092")
        {
            IsUnavailablePlaceholder = true,
            Status = ConnectionState.Failed
        }, mockClient);
        var realCluster = CreateClusterVm("real", "Remote");
        clientFactory.GetAllClients().Returns(new List<IKafkaLensClient> { mockClient });
        clusterFactory.LoadClustersForClientAsync(mockClient).Returns(
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new[] { placeholder }));
        var discovery = new TaskCompletionSource<IReadOnlyList<ClusterViewModel>>();
        clusterFactory.LoadClustersForClientsAsync(Arg.Any<IReadOnlyCollection<string>>()).Returns(discovery.Task);
        var vm = CreateViewModel();

        await vm.LoadClusters();
        vm.OpenCluster(placeholder);
        var originalTab = Assert.Single(vm.OpenedClusters);
        discovery.SetResult(new[] { realCluster });
        await vm.RefreshClustersForClientAsync("Remote");

        Assert.Same(originalTab, Assert.Single(vm.OpenedClusters));
        Assert.Equal(realCluster.Id, originalTab.ClusterId);
        realCluster.Status = ConnectionState.Failed;
        Assert.Equal("Red", originalTab.StatusColor);
    }

    [AvaloniaFact]
    public async Task SelectedIndex_WhenChanged_ShouldUpdateIsCurrentOnTabs()
    {
        // Arrange
        var cluster1 = CreateClusterVm("c1", "Cluster1");
        var cluster2 = CreateClusterVm("c2", "Cluster2");
        var vm = CreateViewModel(new ObservableCollection<ClusterViewModel> { cluster1, cluster2 });
        await vm.LoadClusters();
        vm.OpenCluster(cluster1);
        vm.OpenCluster(cluster2);

        // Assert — second tab should be current (OpenCluster sets SelectedIndex)
        Assert.False(vm.OpenedClusters[0].IsCurrent);
        Assert.True(vm.OpenedClusters[1].IsCurrent);

        // Act — switch to first tab
        vm.SelectedIndex = 0;

        // Assert
        Assert.True(vm.OpenedClusters[0].IsCurrent);
        Assert.False(vm.OpenedClusters[1].IsCurrent);
    }

    [AvaloniaFact]
    public void Constructor_ShouldSetTitleFromAppConfig()
    {
        // Act
        var vm = CreateViewModel();

        // Assert
        Assert.Matches(@"^Test \d+\.\d+\.\d+$", vm.Title!);
    }

    [AvaloniaFact]
    public void Constructor_ShouldSetThemeFromSettings()
    {
        // Arrange
        settingsService.GetValue("Theme").Returns("Dark");

        // Act
        var vm = CreateViewModel();

        // Assert
        Assert.Equal("Dark", vm.CurrentTheme);
    }

    [AvaloniaFact]
    public void Constructor_WhenNoThemeSetting_ShouldDefaultToSystem()
    {
        // Arrange
        settingsService.GetValue("Theme").Returns((string?)null);

        // Act
        var vm = CreateViewModel();

        // Assert
        Assert.Equal("System", vm.CurrentTheme);
    }

    [AvaloniaFact]
    public void CurrentTheme_WhenChanged_ShouldPersistToSettings()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.CurrentTheme = "Dark";

        // Assert
        settingsService.Received(1).SetValue("Theme", "Dark");
    }

    [AvaloniaFact]
    public async Task LoadClusters_WhenInitialResultIsEmpty_ShouldRestoreTabsWhenClusterAppearsLater()
    {
        // Arrange
        var targetCluster = CreateClusterVm("c1", "Cluster1");
        clientFactory.GetAllClients().Returns(new List<IKafkaLensClient> { mockClient });
        clusterFactory.LoadClustersForClientAsync(mockClient).Returns(
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel>()),
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel> { targetCluster }));
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            RestoreTabsOnStartup = true,
            OpenedTabs = new List<OpenedTabState>
            {
                new() { ClusterId = "c1" }
            }
        });

        var vm = CreateViewModel();

        // Act
        await vm.LoadClusters();
        await vm.LoadClusters();

        // Assert
        Assert.Single(vm.OpenedClusters);
        Assert.Equal("c1", vm.OpenedClusters[0].ClusterId);
    }

    [AvaloniaFact]
    public async Task LoadClusters_ShouldRestoreOpenedTabUiState()
    {
        // Arrange
        var targetCluster = CreateClusterVm("c1", "Cluster1");
        clientFactory.GetAllClients().Returns(new List<IKafkaLensClient> { mockClient });
        clusterFactory.LoadClustersForClientAsync(mockClient).Returns(
            Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel> { targetCluster }));
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            RestoreTabsOnStartup = true,
            OpenedTabs = new List<OpenedTabState>
            {
                new()
                {
                    ClusterId = "c1",
                    MessagesSortColumn = "Offset",
                    MessagesSortAscending = false,
                    PositiveFilter = "error",
                    NegativeFilter = "debug",
                    LineFilter = "tenantId",
                    UseObjectFilter = false
                }
            }
        });

        var vm = CreateViewModel();

        // Act
        await vm.LoadClusters();

        // Assert
        Assert.Single(vm.OpenedClusters);
        var opened = vm.OpenedClusters[0];
        Assert.Equal("Offset", opened.MessagesSortColumn);
        Assert.False(opened.MessagesSortAscending);
        Assert.Equal("error", opened.CurrentMessages.PositiveFilter);
        Assert.Equal("debug", opened.CurrentMessages.NegativeFilter);
        Assert.Equal("tenantId", opened.CurrentMessages.LineFilter);
        Assert.False(opened.CurrentMessages.UseObjectFilter);
    }

    [AvaloniaFact]
    public async Task SaveNotification_WhenMessagesSaved_ShouldShowDestinationAndCount()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        WeakReferenceMessenger.Default.Send(new MessagesSavedMessage("C:\\saved", 2));
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Assert
        Assert.True(vm.IsSaveNotificationVisible);
        Assert.Contains("Saved 2 messages to C:\\saved", vm.SaveNotificationMessage);
    }

    [AvaloniaFact]
    public async Task HideSaveNotificationsForSession_ShouldSuppressLaterNotifications()
    {
        // Arrange
        var vm = CreateViewModel();
        vm.HideSaveNotificationsForSessionCommand.Execute(null);

        // Act
        WeakReferenceMessenger.Default.Send(new MessagesSavedMessage("C:\\saved", 1));
        await Dispatcher.UIThread.InvokeAsync(() => { });

        // Assert
        Assert.False(vm.IsSaveNotificationVisible);
        settingsService.DidNotReceive().SaveBrowserConfig(Arg.Any<BrowserConfig>());
    }

    [Fact]
    public void NeverShowSaveNotifications_ShouldPersistSuppression()
    {
        // Arrange
        var vm = CreateViewModel();

        // Act
        vm.NeverShowSaveNotificationsCommand.Execute(null);

        // Assert
        Assert.False(settingsService.GetBrowserConfig().ShowSaveNotification);
        settingsService.Received(1).SaveBrowserConfig(Arg.Is<BrowserConfig>(config => !config.ShowSaveNotification));
        Assert.False(vm.IsSaveNotificationVisible);
    }
}
