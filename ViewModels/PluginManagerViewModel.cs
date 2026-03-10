using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared.Plugins;
using KafkaLens.ViewModels.Services;
using Serilog;

namespace KafkaLens.ViewModels;

// ── Child view-model: one row in the Available tab ──────────────────────────

public partial class AvailablePluginViewModel : ObservableObject
{
    private readonly PluginInstaller _installer;
    private readonly Action _onInstalled;

    public RepositoryPlugin Plugin { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstallable))]
    private string _status = "Install";

    [ObservableProperty] private bool _isInstalling;

    public bool IsInstallable => Status == "Install" || Status == "Update Available";

    /// <summary>True when the plugin has a homepage URL to display.</summary>
    public bool HasHomepage => !string.IsNullOrEmpty(Plugin.Homepage);

    public IAsyncRelayCommand InstallCommand { get; }

    public AvailablePluginViewModel(RepositoryPlugin plugin, string status,
        PluginInstaller installer, Action onInstalled)
    {
        Plugin       = plugin;
        _status      = status;
        _installer   = installer;
        _onInstalled = onInstalled;
        InstallCommand = new AsyncRelayCommand(InstallAsync, CanInstall);
    }

    private bool CanInstall() => Status == "Install" || Status == "Update Available";

    private async Task InstallAsync()
    {
        IsInstalling = true;
        try
        {
            await _installer.InstallAsync(Plugin);
            Status = "Installed";
            InstallCommand.NotifyCanExecuteChanged();
            _onInstalled();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to install plugin {Id}", Plugin.Id);
        }
        finally
        {
            IsInstalling = false;
        }
    }
}

// ── Child view-model: one row in the Installed tab ──────────────────────────

public partial class InstalledPluginViewModel : ObservableObject
{
    private readonly PluginRegistry _registry;
    private readonly PluginInstaller _installer;
    private readonly Action<InstalledPluginViewModel> _onUninstall;

    public PluginInfo Plugin { get; }

    [ObservableProperty] private bool _isEnabled;

    /// <summary>True when the plugin has a homepage URL to display.</summary>
    public bool HasHomepage => !string.IsNullOrEmpty(Plugin.Homepage);

    /// <summary>True when an icon.png exists in the plugin folder.</summary>
    public bool HasIcon => !string.IsNullOrEmpty(Plugin.IconPath);

    public IRelayCommand UninstallCommand { get; }

    public InstalledPluginViewModel(PluginInfo plugin, PluginRegistry registry,
        PluginInstaller installer, Action<InstalledPluginViewModel> onUninstall)
    {
        Plugin       = plugin;
        _isEnabled   = plugin.IsEnabled;
        _registry    = registry;
        _installer   = installer;
        _onUninstall = onUninstall;

        UninstallCommand = new RelayCommand(() =>
        {
            try
            {
                _installer.Uninstall(plugin.FilePath, plugin.FolderPath);
                _onUninstall(this);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to uninstall plugin {Id}", plugin.Id);
            }
        });
    }

    partial void OnIsEnabledChanged(bool value)
    {
        _registry.SetEnabled(Plugin.Id, value);
    }
}

// ── Main Plugin Manager view-model ──────────────────────────────────────────

public partial class PluginManagerViewModel : ViewModelBase
{
    private readonly PluginRegistry _registry;
    private readonly PluginRepositoryClient _repoClient;
    private readonly PluginInstaller _installer;
    private readonly RepositoryManager _repoManager;

    [ObservableProperty] private ObservableCollection<string> _repositories = null!;
    [ObservableProperty] private string? _selectedRepository;
    [ObservableProperty] private ObservableCollection<AvailablePluginViewModel> _availablePlugins = [];
    [ObservableProperty] private ObservableCollection<InstalledPluginViewModel> _installedPlugins = [];
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string _newRepositoryUrl = "";
    [ObservableProperty] private bool _isAddingRepository;

    public IAsyncRelayCommand  RefreshCommand          { get; }
    public IRelayCommand        AddRepositoryCommand    { get; }
    public IRelayCommand<string> RemoveRepositoryCommand { get; }
    public IRelayCommand        ToggleAddRepoCommand    { get; }

    public PluginManagerViewModel(
        PluginRegistry registry,
        PluginRepositoryClient repoClient,
        PluginInstaller installer,
        RepositoryManager repoManager)
    {
        _registry    = registry;
        _repoClient  = repoClient;
        _installer   = installer;
        _repoManager = repoManager;

        RefreshCommand          = new AsyncRelayCommand(RefreshAsync);
        AddRepositoryCommand    = new RelayCommand(AddRepository);
        RemoveRepositoryCommand = new RelayCommand<string>(RemoveRepository);
        ToggleAddRepoCommand    = new RelayCommand(() => IsAddingRepository = !IsAddingRepository);

        LoadRepositories();
        LoadInstalledPlugins();
    }

    private void LoadRepositories()
    {
        var currentRepos = _repoManager.GetRepositories();
        
        // Initialize the collection if it's null, or clear it if it exists
        if (_repositories == null)
        {
            _repositories = new ObservableCollection<string>(currentRepos);
        }
        else
        {
            _repositories.Clear();
            foreach (var repo in currentRepos)
            {
                _repositories.Add(repo);
            }
        }
        
        SelectedRepository = _repositories.FirstOrDefault();
    }

    private void LoadInstalledPlugins()
    {
        var plugins = _registry.GetInstalledPlugins()
            .Select(p => new InstalledPluginViewModel(p, _registry, _installer, RemoveInstalled));
        InstalledPlugins = new ObservableCollection<InstalledPluginViewModel>(plugins);
    }

    private void RemoveInstalled(InstalledPluginViewModel vm)
    {
        Dispatcher.UIThread.Post(() =>
        {
            InstalledPlugins.Remove(vm);
            RefreshAvailableStatuses();
        });
    }

    partial void OnSelectedRepositoryChanged(string? value)
    {
        if (value != null)
            _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (SelectedRepository == null) return;

        IsLoading    = true;
        ErrorMessage = null;

        try
        {
            var index = await _repoClient.FetchAsync(SelectedRepository);

            if (index == null)
            {
                ErrorMessage = $"Failed to fetch repository: {SelectedRepository}";
                AvailablePlugins = [];
                return;
            }

            var installedIds = InstalledPlugins
                .ToDictionary(p => p.Plugin.Id, p => p.Plugin.Version);

            var vms = index.Plugins.Select(p =>
            {
                var status = "Install";
                if (installedIds.TryGetValue(p.Id, out var installedVersion))
                {
                    status = Version.TryParse(p.Version, out var remote) &&
                             Version.TryParse(installedVersion, out var local) &&
                             remote > local
                        ? "Update Available"
                        : "Installed";
                }
                return new AvailablePluginViewModel(p, status, _installer, LoadInstalledPlugins);
            });

            AvailablePlugins = new ObservableCollection<AvailablePluginViewModel>(vms);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error refreshing plugin repository");
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void AddRepository()
    {
        var url = NewRepositoryUrl.Trim();
        if (string.IsNullOrEmpty(url)) return;
        
        // Add to settings (which has duplicate protection)
        _repoManager.AddRepository(url);
        
        // Reload repositories from settings to ensure sync
        LoadRepositories();
        
        // Clear the input
        NewRepositoryUrl = string.Empty;
        IsAddingRepository = false;
    }

    private void RemoveRepository(string? url)
    {
        if (url == null) return;
        _repoManager.RemoveRepository(url);
        
        // Reload repositories from settings to ensure sync
        LoadRepositories();
    }

    private void RefreshAvailableStatuses()
    {
        var installedIds = InstalledPlugins
            .ToDictionary(p => p.Plugin.Id, p => p.Plugin.Version);

        foreach (var vm in AvailablePlugins)
        {
            if (installedIds.TryGetValue(vm.Plugin.Id, out var installedVersion))
            {
                vm.Status = Version.TryParse(vm.Plugin.Version, out var remote) &&
                            Version.TryParse(installedVersion, out var local) &&
                            remote > local
                    ? "Update Available"
                    : "Installed";
            }
            else
            {
                vm.Status = "Install";
            }
            vm.InstallCommand.NotifyCanExecuteChanged();
        }
    }
}
