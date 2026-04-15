using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using KafkaLens.Shared.Models;
using KafkaLens.Shared.Services;
using KafkaLens.ViewModels.Messages;
using KafkaLens.ViewModels.Services;
using Newtonsoft.Json;
using Serilog;
using System.Collections.ObjectModel;

namespace KafkaLens.ViewModels;

public partial class PreferencesViewModel : ViewModelBase
{
    private const string HIDDEN_KEY_FORMATTERS_KEY = "HiddenKeyFormatters";
    private const string HIDDEN_VALUE_FORMATTERS_KEY = "HiddenValueFormatters";

    private readonly ISettingsService settingsService;
    private readonly IThemeService? themeService;
    private readonly IFormatterService? formatterService;

    [ObservableProperty]
    private KafkaConfig kafkaConfig;

    [ObservableProperty]
    private BrowserConfig browserConfig;

    public ObservableCollection<string> Themes { get; } = new();
    public ObservableCollection<FormatterSettingViewModel> FormatterSettings { get; } = new();

    [ObservableProperty]
    private string selectedTheme;

    partial void OnSelectedThemeChanged(string value) => applyTheme?.Invoke(GetThemeIdFromDisplayName(value));

    partial void OnFetchCountsStringChanged(string value) => ValidateAndUpdateFetchCounts(value);

    partial void OnSelectedDefaultFetchCountChanged(int? value)
    {
        if (value.HasValue)
        {
            BrowserConfig.DefaultFetchCount = value.Value;
        }
    }

    [ObservableProperty]
    private string fetchCountsString;

    [ObservableProperty]
    private string fetchCountsError = "";

    public ObservableCollection<int> AvailableFetchCounts { get; } = new();

    [ObservableProperty]
    private int? selectedDefaultFetchCount;

    [ObservableProperty]
    private bool fastConnectionCheck;

    [ObservableProperty]
    private bool deepConnectionCheck;

    [ObservableProperty]
    private int selectedTabIndex;

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand CancelCommand { get; }

    public Action CloseAction { get; set; } = () => { };

    private readonly Action<string>? applyTheme;
    private readonly string originalTheme;

    public PreferencesViewModel(ISettingsService settingsService, IThemeService? themeService = null, Action<string>? applyTheme = null, IFormatterService? formatterService = null)
    {
        this.settingsService = settingsService;
        this.themeService = themeService;
        this.applyTheme = applyTheme;
        this.formatterService = formatterService;

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
        LoadAvailableFetchCounts();
        SelectedDefaultFetchCount = browserConfig.DefaultFetchCount;

        FastConnectionCheck = !browserConfig.EagerLoadTopicsOnStartup;
        DeepConnectionCheck = browserConfig.EagerLoadTopicsOnStartup;

        LoadFormatterSettings();

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

    private void LoadAvailableFetchCounts()
    {
        // Create a new list with the updated values to avoid clearing the collection
        var newCounts = BrowserConfig.FetchCounts.OrderBy(x => x).ToList();

        // Remove items that are no longer in the list
        for (int i = AvailableFetchCounts.Count - 1; i >= 0; i--)
        {
            if (!newCounts.Contains(AvailableFetchCounts[i]))
            {
                AvailableFetchCounts.RemoveAt(i);
            }
        }

        // Add new items in sorted order
        foreach (var count in newCounts)
        {
            if (!AvailableFetchCounts.Contains(count))
            {
                // Find the correct position to insert to maintain sorted order
                int insertIndex = 0;
                for (int j = 0; j < AvailableFetchCounts.Count; j++)
                {
                    if (AvailableFetchCounts[j] < count)
                    {
                        insertIndex = j + 1;
                    }
                    else
                    {
                        break;
                    }
                }
                AvailableFetchCounts.Insert(insertIndex, count);
            }
        }
    }

    private void ValidateAndUpdateFetchCounts(string fetchCountsString)
    {
        if (string.IsNullOrWhiteSpace(fetchCountsString))
        {
            FetchCountsError = "Fetch counts cannot be empty.";
            return;
        }

        List<int> entries;
        try
        {
            entries = fetchCountsString.Split(',')
                .Select(s => s.Trim())
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(int.Parse)
                .ToList();
        }
        catch
        {
            FetchCountsError = "Fetch counts must be a comma-separated list of numbers.";
            return;
        }

        if (entries.Count == 0)
        {
            FetchCountsError = "At least one fetch count must be provided.";
            return;
        }

        if (entries.Any(entry => entry <= 0))
        {
            FetchCountsError = "All fetch counts must be positive numbers.";
            return;
        }

        if (entries.Distinct().Count() != entries.Count)
        {
            FetchCountsError = "Duplicate fetch counts are not allowed.";
            return;
        }

        // Clear error if validation passes
        FetchCountsError = "";

        // Update the browser config
        BrowserConfig.FetchCounts.Clear();
        entries.ForEach(entry => BrowserConfig.FetchCounts.Add(entry));

        // If current default fetch count is not in the new list, set it to the first available
        if (!entries.Contains(BrowserConfig.DefaultFetchCount))
        {
            BrowserConfig.DefaultFetchCount = entries.First();
        }

        // Update the selected item for the ComboBox
        SelectedDefaultFetchCount = BrowserConfig.DefaultFetchCount;

        // Refresh the dropdown after ensuring the default is valid
        LoadAvailableFetchCounts();
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

    private void LoadFormatterSettings()
    {
        if (formatterService == null) return;

        var allNames = formatterService.GetAllFormatterNames();
        var hiddenValues = ParseHiddenSet(settingsService.GetValue(HIDDEN_VALUE_FORMATTERS_KEY));
        var hiddenKeys = ParseHiddenSet(settingsService.GetValue(HIDDEN_KEY_FORMATTERS_KEY));

        FormatterSettings.Clear();
        foreach (var name in allNames)
        {
            FormatterSettings.Add(new FormatterSettingViewModel(
                name,
                isEnabledForValues: !hiddenValues.Contains(name),
                isEnabledForKeys: !hiddenKeys.Contains(name)));
        }
    }

    private void SaveFormatterSettings()
    {
        var hiddenValues = FormatterSettings
            .Where(f => !f.IsEnabledForValues)
            .Select(f => f.Name)
            .ToList();

        var hiddenKeys = FormatterSettings
            .Where(f => !f.IsEnabledForKeys)
            .Select(f => f.Name)
            .ToList();

        settingsService.SetValue(HIDDEN_VALUE_FORMATTERS_KEY, JsonConvert.SerializeObject(hiddenValues));
        settingsService.SetValue(HIDDEN_KEY_FORMATTERS_KEY, JsonConvert.SerializeObject(hiddenKeys));
    }

    private static HashSet<string> ParseHiddenSet(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new HashSet<string>();
        try
        {
            var parsed = JsonConvert.DeserializeObject<List<string>>(raw);
            if (parsed != null)
                return new HashSet<string>(parsed.Where(v => !string.IsNullOrWhiteSpace(v)), StringComparer.Ordinal);
        }
        catch { }
        return new HashSet<string>(
            raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
               .Where(v => !string.IsNullOrWhiteSpace(v)),
            StringComparer.Ordinal);
    }

    private void Save()
    {
        // Check if there are any validation errors
        if (!string.IsNullOrEmpty(FetchCountsError))
        {
            MainViewModel.ShowMessage("Invalid Input", "Please fix the validation errors before saving.");
            return;
        }

        if (BrowserConfig.DefaultFetchCount <= 0)
        {
            MainViewModel.ShowMessage("Invalid Input", "Default fetch count must be a positive number.");
            return;
        }

        // Keep the latest runtime tab state instead of overwriting with a potentially stale dialog snapshot.
        var latestBrowserConfig = settingsService.GetBrowserConfig();
        BrowserConfig.OpenedTabs = latestBrowserConfig.OpenedTabs?.ToList() ?? new List<OpenedTabState>();

        BrowserConfig.EagerLoadTopicsOnStartup = DeepConnectionCheck;

        settingsService.SaveKafkaConfig(KafkaConfig);
        settingsService.SaveBrowserConfig(BrowserConfig);
        SaveFormatterSettings();

        WeakReferenceMessenger.Default.Send(new ConfigurationChangedMessage());

        CloseAction();
    }

    private void Cancel()
    {
        applyTheme?.Invoke(originalTheme);
        CloseAction();
    }
}