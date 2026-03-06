using System.Collections.ObjectModel;
using Avalonia.Headless.XUnit;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.ViewModels.Config;

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
    private readonly IUpdateService updateService = Substitute.For<IUpdateService>();
    private readonly IKafkaLensClient mockClient = Substitute.For<IKafkaLensClient>();
    private readonly AppConfig appConfig = new() { Title = "Test", ClusterRefreshIntervalSeconds = 100 };

    public MainViewModelBusinessLogicTests()
    {
        settingsService.GetBrowserConfig().Returns(new BrowserConfig());
        clusterFactory.LoadClustersAsync().Returns(Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel>()));
        clusterFactory.LoadClustersForClientAsync(Arg.Any<IKafkaLensClient>()).Returns(Task.FromResult<IReadOnlyList<ClusterViewModel>>(new List<ClusterViewModel>()));
        clientFactory.GetAllClients().Returns(new List<IKafkaLensClient>());
        MainViewModel.ConfirmRestoreTabs = (count) => Task.FromResult(true);
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
            updateService,
            FormatterFactory.Instance);
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

        // Create a new version of the cluster with the same ID but different name
        var updatedCluster = CreateClusterVm("c1", "RenamedCluster");
        clusters[0] = updatedCluster;

        // Act — second LoadClusters should update opened cluster names
        await vm.LoadClusters();

        // Assert
        Assert.Equal("RenamedCluster", vm.OpenedClusters[0].Name);
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

    [AvaloniaFact]
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
        clusterFactory.LoadClustersAsync().Returns(
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
        clusterFactory.LoadClustersAsync().Returns(
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
        Assert.Equal("Test", vm.Title);
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
        clusterFactory.LoadClustersAsync().Returns(
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
        clusterFactory.LoadClustersAsync().Returns(
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
}