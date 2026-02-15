using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KafkaLens.Shared;

namespace KafkaLens.ViewModels;

public partial class UpdateViewModel : ViewModelBase
{
    [ObservableProperty] private string? latestVersion;
    [ObservableProperty] private string? releaseNotes;
    [ObservableProperty] private bool updateAvailable;

    public UpdateCheckResult Result { get; }

    public IRelayCommand UpdateCommand { get; }
    public IRelayCommand SkipCommand { get; }

    public event Action? OnUpdate;
    public event Action? OnSkip;

    public UpdateViewModel(UpdateCheckResult result)
    {
        Result = result;
        LatestVersion = result.LatestVersion;
        ReleaseNotes = result.ReleaseNotes;
        UpdateAvailable = result.UpdateAvailable;

        UpdateCommand = new RelayCommand(() => OnUpdate?.Invoke());
        SkipCommand = new RelayCommand(() => OnSkip?.Invoke());
    }
}
