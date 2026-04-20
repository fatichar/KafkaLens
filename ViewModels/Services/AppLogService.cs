using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

namespace KafkaLens.ViewModels.Services;

public sealed partial class AppLogService : ObservableObject, IAppLogService
{
    private const int DefaultMaxEntries = 500;
    private readonly int maxEntries;

    public ObservableCollection<AppLogEntry> Entries { get; } = new();

    [ObservableProperty] private AppLogEntry? latestEntry;

    public AppLogService() : this(DefaultMaxEntries)
    {
    }

    internal AppLogService(int maxEntries)
    {
        this.maxEntries = Math.Max(1, maxEntries);
    }

    public void LogInfo(string message, string? source = null) =>
        Add(AppLogLevel.Info, message, source);

    public void LogWarning(string message, string? source = null) =>
        Add(AppLogLevel.Warning, message, source);

    public void LogError(string message, string? source = null) =>
        Add(AppLogLevel.Error, message, source);

    public void Clear() => OnUiThread(() =>
    {
        Entries.Clear();
        LatestEntry = null;
    });

    private void Add(AppLogLevel level, string message, string? source)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        var entry = new AppLogEntry(DateTime.Now, level, message, source);
        OnUiThread(() =>
        {
            Entries.Add(entry);
            while (Entries.Count > maxEntries)
                Entries.RemoveAt(0);

            LatestEntry = entry;
        });
    }

    private static void OnUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
            action();
        else
            Dispatcher.UIThread.Post(action);
    }
}
