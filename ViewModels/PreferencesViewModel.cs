using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Services;
using KafkaLens.ViewModels.Messages;
using Serilog;
using System.Collections.ObjectModel;

namespace KafkaLens.ViewModels;

public partial class PreferencesViewModel : ViewModelBase
{
    private readonly ISettingsService settingsService;
    private readonly IThemeService? themeService;

    [ObservableProperty]
    private KafkaConfig kafkaConfig;

    [ObservableProperty]
    private BrowserConfig browserConfig;

    public ObservableCollection<string> Themes { get; } = new();

    [ObservableProperty]
    private string selectedTheme;

    partial void OnSelectedThemeChanged(string value) => applyTheme?.Invoke(GetThemeIdFromDisplayName(value));

    [ObservableProperty]
    private string fetchCountsString;

    [ObservableProperty]
    private bool fastConnectionCheck;

    [ObservableProperty]
    private bool deepConnectionCheck;

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public Action CloseAction { get; set; } = () => { };

    private readonly Action<string>? applyTheme;
    private readonly string originalTheme;

    public PreferencesViewModel(ISettingsService settingsService, IThemeService? themeService = null, Action<string>? applyTheme = null)
    {
        this.settingsService = settingsService;
        this.themeService = themeService;
        this.applyTheme = applyTheme;

        kafkaConfig = settingsService.GetKafkaConfig();
        browserConfig = settingsService.GetBrowserConfig();
        
        // Load themes first to validate the current theme
        LoadThemes(themeService);
        
        // Get current theme from settings, but validate it exists
        var currentTheme = settingsService.GetValue("Theme") ?? "System";
        var validatedThemeId = ValidateTheme(currentTheme, themeService);
        
        // Convert theme ID to DisplayName for selection in the dialog
        selectedTheme = originalTheme = GetThemeDisplayName(validatedThemeId, themeService);
        
        fetchCountsString = string.Join(", ", browserConfig.FetchCounts);

        FastConnectionCheck = !browserConfig.EagerLoadTopicsOnStartup;
        DeepConnectionCheck = browserConfig.EagerLoadTopicsOnStartup;

        SaveCommand = new RelayCommand(Save);
        CancelCommand = new RelayCommand(Cancel);
    }

    private string ValidateTheme(string themeName, IThemeService? themeService)
    {
        if (themeService != null)
        {
            var availableThemes = themeService.GetAvailableThemes();
            // Try exact match first, then case-insensitive
            var theme = availableThemes.FirstOrDefault(t => t.Id == themeName) ??
                       availableThemes.FirstOrDefault(t => string.Equals(t.Id, themeName, StringComparison.OrdinalIgnoreCase));
            
            if (theme != null)
            {
                return theme.Id; // Return the actual theme ID (case-corrected)
            }
        }
        
        // If theme not found or ThemeService unavailable, fall back to System
        if (themeName != "System")
        {
            Log.Warning("Theme {ThemeName} not found, falling back to System theme", themeName);
            return "System";
        }
        
        return "System";
    }

    private void LoadThemes(IThemeService? themeService)
    {
        Themes.Clear();
        
        if (themeService != null)
        {
            var availableThemes = themeService.GetAvailableThemes();
            foreach (var theme in availableThemes.OrderBy(t => t.DisplayName))
            {
                Themes.Add(theme.DisplayName);
            }
        }
        else
        {
            // Fallback to basic themes if ThemeService is not available
            Themes.Add("Light");
            Themes.Add("Dark");
            Themes.Add("System");
        }
    }

    private string GetThemeDisplayName(string themeId, IThemeService? themeService)
    {
        if (themeService != null)
        {
            var availableThemes = themeService.GetAvailableThemes();
            var theme = availableThemes.FirstOrDefault(t => string.Equals(t.Id, themeId, StringComparison.OrdinalIgnoreCase));
            if (theme != null)
            {
                return theme.DisplayName;
            }
        }
        
        // Fallback for basic themes or if ThemeService unavailable
        return themeId;
    }

    private string GetThemeIdFromDisplayName(string displayName)
    {
        if (themeService != null)
        {
            var availableThemes = themeService.GetAvailableThemes();
            var theme = availableThemes.FirstOrDefault(t => string.Equals(t.DisplayName, displayName, StringComparison.OrdinalIgnoreCase));
            if (theme != null)
            {
                return theme.Id;
            }
        }
        
        // Fallback for basic themes or if ThemeService unavailable
        return displayName;
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

        BrowserConfig.EagerLoadTopicsOnStartup = DeepConnectionCheck;

        settingsService.SaveKafkaConfig(KafkaConfig);
        settingsService.SaveBrowserConfig(BrowserConfig);

        WeakReferenceMessenger.Default.Send(new ConfigurationChangedMessage());

        CloseAction();
    }

    private void Cancel()
    {
        applyTheme?.Invoke(originalTheme);
        CloseAction();
    }
}