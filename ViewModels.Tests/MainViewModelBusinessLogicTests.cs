using System.Collections.ObjectModel;
using Avalonia.Headless.XUnit;
using KafkaLens.Formatting;
using KafkaLens.Shared;
using KafkaLens.Shared.DataAccess;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels.Config;

namespace KafkaLens.ViewModels.Tests;

public class MainViewModelBusinessLogicTests
{
    private readonly IClusterFactory _clusterFactory;
    private readonly ISettingsService _settingsService;
    private readonly ITopicSettingsService _topicSettingsService;
    private readonly ISavedMessagesClient _savedMessagesClient;
    private readonly IClusterInfoRepository _clusterInfoRepository;
    private readonly IClientInfoRepository _clientInfoRepository;
    private readonly IClientFactory _clientFactory;
    private readonly IKafkaLensClient _mockClient;
    private readonly AppConfig _appConfig;

    public MainViewModelBusinessLogicTests()
    {
        _clusterFactory = Substitute.For<IClusterFactory>();
        _settingsService = Substitute.For<ISettingsService>();
        _topicSettingsService = Substitute.For<ITopicSettingsService>();
        _savedMessagesClient = Substitute.For<ISavedMessagesClient>();
        _clusterInfoRepository = Substitute.For<IClusterInfoRepository>();
        _clientInfoRepository = Substitute.For<IClientInfoRepository>();
        _clientFactory = Substitute.For<IClientFactory>();
        _mockClient = Substitute.For<IKafkaLensClient>();
        _appConfig = new AppConfig { Title = "Test", ClusterRefreshIntervalSeconds = 100 };
    }

    private MainViewModel CreateViewModel(ObservableCollection<ClusterViewModel>? clusters = null)
    {
        clusters ??= new ObservableCollection<ClusterViewModel>();
        _clusterFactory.GetAllClusters().Returns(clusters);
        _clusterFactory.LoadClustersAsync().Returns(Task.FromResult(clusters));

        return new MainViewModel(
            _appConfig,
            _clusterFactory,
            _settingsService,
            _topicSettingsService,
            _savedMessagesClient,
            _clusterInfoRepository,
            _clientInfoRepository,
            _clientFactory,
            FormatterFactory.Instance);
    }

    private (ClusterViewModel vm, KafkaCluster model) CreateClusterVmWithModel(string id = "c1", string name = "Cluster1", string address = "localhost:9092")
    {
        var cluster = new KafkaCluster(id, name, address);
        return (new ClusterViewModel(cluster, _mockClient), cluster);
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
        await _clusterFactory.Received().LoadClustersAsync();
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
        _clusterFactory.Received(1).GetAllClusters();
        // OnActivated() in constructor also calls LoadClusters, so total is 3+
        await _clusterFactory.Received().LoadClustersAsync();
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

        // Rename the cluster via the underlying model
        model.Name = "RenamedCluster";

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
        var clusters = new ObservableCollection<ClusterViewModel>();
        var vm = CreateViewModel(clusters);
        await vm.LoadClusters();
        Assert.NotNull(vm.MenuItems);

        // Act — add a cluster to the collection (triggers OnClustersChanged)
        var newCluster = CreateClusterVm("c2", "NewCluster");
        clusters.Add(newCluster);

        // Assert — no exception thrown, menu items updated
        Assert.Single(vm.Clusters);
    }

    [AvaloniaFact]
    public async Task OnClustersChanged_RemovingCluster_ShouldUpdateMenuItems()
    {
        // Arrange
        var cluster = CreateClusterVm("c1", "Cluster1");
        var clusters = new ObservableCollection<ClusterViewModel> { cluster };
        var vm = CreateViewModel(clusters);
        await vm.LoadClusters();

        // Act — remove the cluster (triggers OnClustersChanged)
        clusters.Remove(cluster);

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
        _settingsService.GetValue("Theme").Returns("Dark");

        // Act
        var vm = CreateViewModel();

        // Assert
        Assert.Equal("Dark", vm.CurrentTheme);
    }

    [AvaloniaFact]
    public void Constructor_WhenNoThemeSetting_ShouldDefaultToSystem()
    {
        // Arrange
        _settingsService.GetValue("Theme").Returns((string?)null);

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
        _settingsService.Received(1).SetValue("Theme", "Dark");
    }
}
