using CommunityToolkit.Mvvm.ComponentModel;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Services;
using NSubstitute;
using Xunit;

namespace KafkaLens.ViewModels.Tests;

public class PreferencesViewModelTests
{
    [Fact]
    public void Save_WhenValidationErrorsExist_ShouldNotSave()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            DefaultFetchCount = 10,
            FetchCounts = new SortedSet<int> { 10, 25, 50 }
        });
        settingsService.GetValue("Theme").Returns("System");

        var vm = new PreferencesViewModel(settingsService);
        
        // Set invalid fetch counts to trigger validation error
        vm.FetchCountsString = "invalid, input";

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
        Assert.Equal("Please fix the validation errors before saving.", message);
        settingsService.DidNotReceive().SaveBrowserConfig(Arg.Any<BrowserConfig>());
        settingsService.DidNotReceive().SaveKafkaConfig(Arg.Any<KafkaConfig>());
    }

    [Fact]
    public void Save_ShouldPreserveLatestOpenedTabs()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());

        var configFromDialogOpen = new BrowserConfig
        {
            DefaultFetchCount = 10,
            FetchCounts = new SortedSet<int> { 10, 25, 50 },
            OpenedTabs = new List<OpenedTabState>
            {
                new() { ClusterId = "stale-cluster-id", PositiveFilter = "stale" }
            }
        };

        var latestConfigAtSaveTime = new BrowserConfig
        {
            DefaultFetchCount = 10,
            FetchCounts = new SortedSet<int> { 10, 25, 50 },
            OpenedTabs = new List<OpenedTabState>
            {
                new() { ClusterId = "c1", PositiveFilter = "err" },
                new() { ClusterId = "c2", PositiveFilter = "warn" }
            }
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
                config.OpenedTabs.Count == 2 &&
                config.OpenedTabs[0].ClusterId == "c1" &&
                config.OpenedTabs[0].PositiveFilter == "err" &&
                config.OpenedTabs[1].ClusterId == "c2" &&
                config.OpenedTabs[1].PositiveFilter == "warn"));
    }

    [Fact]
    public void FetchCountsString_WhenValidInput_UpdatesDropdownAndClearsError()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            FetchCounts = new SortedSet<int> { 10, 25, 50, 100 },
            DefaultFetchCount = 10
        });
        settingsService.GetValue("Theme").Returns("System");

        var vm = new PreferencesViewModel(settingsService);
        var validInput = "5, 15, 30, 60";

        // Act
        vm.FetchCountsString = validInput;

        // Assert
        Assert.Equal("", vm.FetchCountsError);
        Assert.Equal(4, vm.AvailableFetchCounts.Count);
        Assert.Contains(5, vm.AvailableFetchCounts);
        Assert.Contains(15, vm.AvailableFetchCounts);
        Assert.Contains(30, vm.AvailableFetchCounts);
        Assert.Contains(60, vm.AvailableFetchCounts);
    }

    [Fact]
    public void FetchCountsString_WhenEmptyInput_SetsError()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            FetchCounts = new SortedSet<int> { 10, 25, 50, 100 },
            DefaultFetchCount = 10
        });
        settingsService.GetValue("Theme").Returns("System");

        var vm = new PreferencesViewModel(settingsService);
        var emptyInput = "";

        // Act
        vm.FetchCountsString = emptyInput;

        // Assert
        Assert.Equal("Fetch counts cannot be empty.", vm.FetchCountsError);
    }

    [Fact]
    public void FetchCountsString_WhenInvalidNumbers_SetsError()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            FetchCounts = new SortedSet<int> { 10, 25, 50, 100 },
            DefaultFetchCount = 10
        });
        settingsService.GetValue("Theme").Returns("System");

        var vm = new PreferencesViewModel(settingsService);
        var invalidInput = "10, abc, 30";

        // Act
        vm.FetchCountsString = invalidInput;

        // Assert
        Assert.Equal("Fetch counts must be a comma-separated list of numbers.", vm.FetchCountsError);
    }

    [Fact]
    public void FetchCountsString_WhenNegativeNumbers_SetsError()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            FetchCounts = new SortedSet<int> { 10, 25, 50, 100 },
            DefaultFetchCount = 10
        });
        settingsService.GetValue("Theme").Returns("System");

        var vm = new PreferencesViewModel(settingsService);
        var negativeInput = "10, -5, 30";

        // Act
        vm.FetchCountsString = negativeInput;

        // Assert
        Assert.Equal("All fetch counts must be positive numbers.", vm.FetchCountsError);
    }

    [Fact]
    public void FetchCountsString_WhenDuplicates_SetsError()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            FetchCounts = new SortedSet<int> { 10, 25, 50, 100 },
            DefaultFetchCount = 10
        });
        settingsService.GetValue("Theme").Returns("System");

        var vm = new PreferencesViewModel(settingsService);
        var duplicateInput = "10, 10, 30";

        // Act
        vm.FetchCountsString = duplicateInput;

        // Assert
        Assert.Equal("Duplicate fetch counts are not allowed.", vm.FetchCountsError);
    }

    [Fact]
    public void FetchCountsString_WhenDefaultNotInList_UpdatesDefaultToFirst()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            FetchCounts = new SortedSet<int> { 10, 25, 50, 100 },
            DefaultFetchCount = 10
        });
        settingsService.GetValue("Theme").Returns("System");

        var vm = new PreferencesViewModel(settingsService);
        var newInput = "20, 40, 80";
        var originalDefault = vm.BrowserConfig.DefaultFetchCount;

        // Act
        vm.FetchCountsString = newInput;

        // Assert
        Assert.Equal(20, vm.BrowserConfig.DefaultFetchCount);
        Assert.NotEqual(originalDefault, vm.BrowserConfig.DefaultFetchCount);
        Assert.Equal("", vm.FetchCountsError); // No validation errors
        Assert.Equal(3, vm.AvailableFetchCounts.Count); // Dropdown updated
    }

    [Fact]
    public void FetchCountsString_WhenValidationFails_DoesNotUpdateDropdown()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            FetchCounts = new SortedSet<int> { 10, 25, 50, 100 },
            DefaultFetchCount = 10
        });
        settingsService.GetValue("Theme").Returns("System");

        var vm = new PreferencesViewModel(settingsService);
        var originalAvailableCount = vm.AvailableFetchCounts.Count;
        var originalDefault = vm.BrowserConfig.DefaultFetchCount;

        // Act
        vm.FetchCountsString = "invalid, input";

        // Assert
        Assert.NotEqual("", vm.FetchCountsError); // Has validation error
        Assert.Equal(originalAvailableCount, vm.AvailableFetchCounts.Count); // Dropdown not updated
        Assert.Equal(originalDefault, vm.BrowserConfig.DefaultFetchCount); // Default not changed
    }

    [Fact]
    public void AvailableFetchCounts_ShouldBeSortedInAscendingOrder()
    {
        // Arrange
        var settingsService = Substitute.For<ISettingsService>();
        settingsService.GetKafkaConfig().Returns(new KafkaConfig());
        settingsService.GetBrowserConfig().Returns(new BrowserConfig
        {
            FetchCounts = new SortedSet<int> { 100, 10, 50, 25 }, // Unordered input
            DefaultFetchCount = 10
        });
        settingsService.GetValue("Theme").Returns("System");

        var vm = new PreferencesViewModel(settingsService);

        // Assert - Verify the dropdown is sorted in ascending order
        Assert.Equal(4, vm.AvailableFetchCounts.Count);
        Assert.Equal(10, vm.AvailableFetchCounts[0]);
        Assert.Equal(25, vm.AvailableFetchCounts[1]);
        Assert.Equal(50, vm.AvailableFetchCounts[2]);
        Assert.Equal(100, vm.AvailableFetchCounts[3]);

        // Verify each item is less than or equal to the next item
        for (int i = 0; i < vm.AvailableFetchCounts.Count - 1; i++)
        {
            Assert.True(vm.AvailableFetchCounts[i] <= vm.AvailableFetchCounts[i + 1],
                $"Item at index {i} ({vm.AvailableFetchCounts[i]}) should be <= item at index {i + 1} ({vm.AvailableFetchCounts[i + 1]})");
        }
    }
}
