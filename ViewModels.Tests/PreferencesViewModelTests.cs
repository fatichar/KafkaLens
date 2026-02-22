namespace KafkaLens.ViewModels.Tests;

public class PreferencesViewModelTests
{
    [Fact]
    public void Save_WhenDefaultFetchCountIsNotInAvailableFetchCounts_ShouldShowValidationErrorAndNotSave()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            DefaultFetchCount = 10,
            FetchCounts = new SortedSet<int> { 10, 25, 50 },
            OpenedClusterIds = new List<string> { "c1" }
        });

        var vm = new PreferencesViewModel(settingsService)
        {
            FetchCountsString = "10, 25, 50"
        };
        vm.BrowserConfig.DefaultFetchCount = 100;

        string? title = null;
        string? message = null;
        var originalShowMessage = MainViewModel.ShowMessage;
        MainViewModel.ShowMessage = (t, m) =>
        {
            title = t;
            message = m;
        };

        try
        {
            // Act
            vm.SaveCommand.Execute(null);
        }
        finally
        {
            MainViewModel.ShowMessage = originalShowMessage;
        }

        // Assert
        Assert.Equal("Invalid Input", title);
        Assert.Equal("Default fetch count must be one of the available fetch counts.", message);
        settingsService.DidNotReceive().SaveBrowserConfig(Arg.Any<BrowserConfig>());
        settingsService.DidNotReceive().SaveKafkaConfig(Arg.Any<KafkaConfig>());
    }

    [Fact]
    public void Save_ShouldPreserveLatestOpenedClusterIds()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());

        var configFromDialogOpen = new BrowserConfig
        {
            DefaultFetchCount = 10,
            FetchCounts = new SortedSet<int> { 10, 25, 50 },
            OpenedClusterIds = new List<string> { "stale-cluster-id" }
        };

        var latestConfigAtSaveTime = new BrowserConfig
        {
            DefaultFetchCount = 10,
            FetchCounts = new SortedSet<int> { 10, 25, 50 },
            OpenedClusterIds = new List<string> { "c1", "c2" }
        };

        settingsService.GetBrowserConfig().Returns(configFromDialogOpen, latestConfigAtSaveTime);

        var vm = new PreferencesViewModel(settingsService)
        {
            FetchCountsString = "10, 25, 50"
        };

        // Act
        vm.SaveCommand.Execute(null);

        // Assert
        settingsService.Received(1).SaveBrowserConfig(
            Arg.Is<BrowserConfig>(config =>
                config.OpenedClusterIds.SequenceEqual(new[] { "c1", "c2" })));
    }
}
