using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using KafkaLens.ViewModels.Messages;
using System.Collections.ObjectModel;

namespace KafkaLens.ViewModels;

public partial class PreferencesViewModel : ViewModelBase
{
    private readonly ISettingsService settingsService;

    [ObservableProperty]
    private KafkaConfig kafkaConfig;

    [ObservableProperty]
    private BrowserConfig browserConfig;

    public ObservableCollection<string> Themes { get; } = new()
    {
        "Light", "Bright", "Ocean", "Forest", "Purple", "Dark", "System"
    };

    [ObservableProperty]
    private string selectedTheme;

    [ObservableProperty]
    private string fetchCountsString;

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public Action CloseAction { get; set; } = () => { };

    public PreferencesViewModel(ISettingsService settingsService)
    {
        this.settingsService = settingsService;

        kafkaConfig = settingsService.GetKafkaConfig();
        browserConfig = settingsService.GetBrowserConfig();
        selectedTheme = settingsService.GetValue("Theme") ?? "System";
        fetchCountsString = string.Join(", ", browserConfig.FetchCounts);

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
    }

    private void Save()
    {
        List<int> entries;
        try
        {
            entries = FetchCountsString.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(int.Parse)
                .ToList();
        }
        catch
        {
            MainViewModel.ShowMessage("Invalid Input", "Fetch counts must be a comma-separated list of numbers.");
            return;
        }

        if (entries.Count == 0 || entries.Any(entry => entry <= 0))
        {
            MainViewModel.ShowMessage("Invalid Input", "Fetch counts must be positive numbers.");
            return;
        }

        if (BrowserConfig.DefaultFetchCount <= 0)
        {
            MainViewModel.ShowMessage("Invalid Input", "Default fetch count must be a positive number.");
            return;
        }

        if (!entries.Contains(BrowserConfig.DefaultFetchCount))
        {
            MainViewModel.ShowMessage("Invalid Input", "Default fetch count must be one of the available fetch counts.");
            return;
        }

        BrowserConfig.FetchCounts.Clear();
        entries.ForEach(entry => BrowserConfig.FetchCounts.Add(entry));

        // Keep the latest runtime tab state instead of overwriting with a potentially stale dialog snapshot.
        var latestBrowserConfig = settingsService.GetBrowserConfig();
        BrowserConfig.OpenedTabs = latestBrowserConfig.OpenedTabs?.ToList() ?? new List<OpenedTabState>();

        settingsService.SaveKafkaConfig(KafkaConfig);
        settingsService.SaveBrowserConfig(BrowserConfig);
        settingsService.SetValue("Theme", SelectedTheme);

        WeakReferenceMessenger.Default.Send(new ThemeChangedMessage(SelectedTheme));
        WeakReferenceMessenger.Default.Send(new ConfigurationChangedMessage());

        CloseAction();
    }

    private void Cancel()
    {
        CloseAction();
    }
}
