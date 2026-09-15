using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel
{
    private readonly ObservableCollection<MenuItemViewModel> openClusterMenuItems = new();
    private readonly Dictionary<ClusterViewModel, System.ComponentModel.PropertyChangedEventHandler> openClusterMenuHandlers = new();
    private readonly Dictionary<string, bool> clientEnabledByName = new(StringComparer.Ordinal);
    private MenuItemViewModel openMenu = null!;
    private MenuItemViewModel closeTabMenuItem = null!;
    private MenuItemViewModel appLogMenuItem = null!;

    private void CreateMenuItems()
    {
        MenuItems = new ObservableCollection<MenuItemViewModel>
        {
            CreateClusterMenu(),
            CreateEditMenu(),
            CreateViewMenu(),
            CreateHelpMenu()
        };

        UpdateThemeMenuCheckedState();
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
                new MenuItemViewModel { Header = "_Edit Clusters", Command = EditClustersCommand, Gesture = KeyGesture.Parse("Ctrl+E") },
                openMenu,
                new MenuItemViewModel { Header = "_Open Saved Messages", Command = OpenSavedMessagesCommand, Gesture = KeyGesture.Parse("Ctrl+O") },
                closeTabMenuItem
            }
        };
    }

    private MenuItemViewModel CreateOpenMenu()
    {
        var menu = new MenuItemViewModel
        {
            Header = "_Open Cluster",
            Items = openClusterMenuItems
        };

        UpdateOpenMenuItems();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IsLoadingClusters))
                UpdateOpenMenuItems();
        };

        return menu;
    }

    internal void RefreshClientEnabledIndex()
    {
        clientEnabledByName.Clear();
        var clients = ClientInfoRepository.GetAll();
        if (clients == null) return;

        foreach (var client in clients.Values)
            clientEnabledByName[client.Name] = client.IsEnabled;
    }

    private bool IsClientEnabled(string clientName)
    {
        return !clientEnabledByName.TryGetValue(clientName, out var isEnabled) || isEnabled;
    }

    private bool IsClusterAvailable(ClusterViewModel cluster) => cluster.IsAvailable;

    public void RefreshAvailability()
    {
        RefreshClientEnabledIndex();
        foreach (var cluster in Clusters)
        {
            ApplyClientAvailability(cluster);
            if (!cluster.IsAvailable) CloseTabsForCluster(cluster.Id);
        }
        ReconcileOpenClusterMenu();
    }

    private void ApplyClientAvailability(ClusterViewModel cluster)
    {
        cluster.IsClientEnabled = (cluster.Client.Name == "Local" && cluster.Client.CanEditClusters) ||
            IsClientEnabled(cluster.Client.Name);
    }

    private void AddClusterToMenu(ClusterViewModel cluster)
    {
        if (!IsClusterAvailable(cluster) || openClusterMenuItems.Any(m => (m.CommandParameter as string) == cluster.Id))
            return;
        openClusterMenuItems.Add(CreateOpenMenuItem(cluster));
    }

    private void RemoveClusterFromMenu(ClusterViewModel cluster)
    {
        var menuItem = openClusterMenuItems.FirstOrDefault(m => (m.CommandParameter as string) == cluster.Id || m.Header == cluster.Name);
        if (menuItem != null)
            openClusterMenuItems.Remove(menuItem);

        if (openClusterMenuHandlers.Remove(cluster, out var handler))
            cluster.PropertyChanged -= handler;
    }

    private void OnClusterPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (sender is ClusterViewModel cluster && e.PropertyName == nameof(ClusterViewModel.IsAvailable))
        {
            if (IsClusterAvailable(cluster))
            {
                AddClusterToMenu(cluster);
            }
            else
            {
                RemoveClusterFromMenu(cluster);
            }
        }
    }

    private MenuItemViewModel CreateOpenMenuItem(ClusterViewModel c)
    {
        var statusIcon = new StatusIconViewModel { Status = c.Status };
        var menuItem = new MenuItemViewModel
        {
            Header = c.Name,
            Command = OpenClusterCommand,
            CommandParameter = c.Id,
            IsEnabled = true,
            Icon = statusIcon
        };

        System.ComponentModel.PropertyChangedEventHandler handler = (_, e) =>
        {
            if (e.PropertyName == nameof(ClusterViewModel.Status))
                statusIcon.Status = c.Status;
            else if (e.PropertyName == nameof(ClusterViewModel.Name))
                menuItem.Header = c.Name;
        };
        c.PropertyChanged += handler;
        openClusterMenuHandlers[c] = handler;

        return menuItem;
    }

    private void ReconcileOpenClusterMenu()
    {
        var loading = openClusterMenuItems.FirstOrDefault(m => m.Header == "Loading...");
        foreach (var (cluster, handler) in openClusterMenuHandlers)
            cluster.PropertyChanged -= handler;
        openClusterMenuHandlers.Clear();
        openClusterMenuItems.Clear();
        if (loading != null && IsLoadingClusters)
            openClusterMenuItems.Add(loading);
        foreach (var cluster in Clusters.Where(IsClusterAvailable))
            openClusterMenuItems.Add(CreateOpenMenuItem(cluster));
    }

    private void UpdateOpenMenuItems()
    {
        if (IsLoadingClusters && Clusters.Count == 0)
        {
            if (!openClusterMenuItems.Any(m => m.Header == "Loading..."))
                openClusterMenuItems.Add(new MenuItemViewModel { Header = "Loading...", IsEnabled = false });
            return;
        }

        ReconcileOpenClusterMenu();
    }

    private void UpdateCloseTabEnabled()
    {
        if (closeTabMenuItem != null)
            closeTabMenuItem.IsEnabled = OpenedClusters.Count > 0;
    }

    private MenuItemViewModel CreateEditMenu() => new()
    {
        Header = "_Edit",
        Items = new()
        {
            new MenuItemViewModel { Header = "_Preferences", Command = ShowPreferencesCommand },
            new MenuItemViewModel { Header = "Plugin _Manager\u2026", Command = ShowPluginManagerCommand }
        }
    };

    private MenuItemViewModel CreateViewMenu() => new()
    {
        Header = "_View",
        Items = new()
        {
            new MenuItemViewModel { Header = "Theme", Items = CreateThemeMenuItems() },
            CreateAppLogMenuItem()
        }
    };

    private MenuItemViewModel CreateAppLogMenuItem()
    {
        appLogMenuItem = new MenuItemViewModel
        {
            Header = "_Application Log",
            Command = ToggleAppLogPanelCommand,
            ToggleType = MenuItemToggleType.CheckBox,
            IsChecked = IsAppLogPanelVisible
        };
        return appLogMenuItem;
    }

    private ObservableCollection<MenuItemViewModel> CreateThemeMenuItems()
    {
        var items = new ObservableCollection<MenuItemViewModel>();
        
        // Get all available themes from ThemeService (built-in + plugin themes)
        if (_themeService != null)
        {
            var availableThemes = _themeService.GetAvailableThemes();
            
            foreach (var theme in availableThemes)
            {
                items.Add(new MenuItemViewModel
                {
                    Header = theme.DisplayName,
                    CommandParameter = theme.Id, // Store the theme ID for later comparison
                    Command = new RelayCommand(() => CurrentTheme = theme.Id),
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = theme.Id == CurrentTheme
                });
            }
        }
        else
        {
            // Fallback if ThemeService is not available yet
            Log.Warning("ThemeService is null, using fallback themes");
            var fallbackThemes = new[] { "Light", "Dark", "System" };
            foreach (var theme in fallbackThemes)
            {
                items.Add(new MenuItemViewModel
                {
                    Header = theme,
                    CommandParameter = theme, // Store the theme ID for later comparison
                    Command = new RelayCommand(() => CurrentTheme = theme),
                    ToggleType = MenuItemToggleType.Radio,
                    IsChecked = theme == CurrentTheme
                });
            }
        }
        
        return items;
    }

    private MenuItemViewModel CreateHelpMenu() => new()
    {
        Header = "_Help",
        Items = new()
        {
            new MenuItemViewModel { Header = "Check for _Updates", Command = CheckForUpdatesCommand },
            new MenuItemViewModel
            {
                Header = "Auto-check for Updates",
                Command = new RelayCommand(() => AutoCheckForUpdates = !AutoCheckForUpdates),
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = AutoCheckForUpdates
            },
            new MenuItemViewModel { Header = "_About", Command = new RelayCommand(() => ShowAboutDialog()) }
        }
    };

    private void UpdateThemeMenuCheckedState()
    {
        var themeMenu = MenuItems?.FirstOrDefault(m => m.Header == "_View")
            ?.Items?.FirstOrDefault(m => m.Header == "Theme");
        if (themeMenu?.Items != null)
        {
            foreach (var item in themeMenu.Items)
            {
                // Compare using the stored theme ID in CommandParameter property
                var themeId = item.CommandParameter as string;
                item.IsChecked = string.Equals(themeId, CurrentTheme, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    private void UpdateAutoCheckMenuCheckedState()
    {
        var autoCheckMenu = MenuItems?.FirstOrDefault(m => m.Header == "_Help")
            ?.Items?.FirstOrDefault(m => m.Header?.Contains("Auto-check") == true);
        if (autoCheckMenu != null)
            autoCheckMenu.IsChecked = AutoCheckForUpdates;
    }

    partial void OnIsAppLogPanelVisibleChanged(bool value)
    {
        if (appLogMenuItem != null)
            appLogMenuItem.IsChecked = value;
    }
}
