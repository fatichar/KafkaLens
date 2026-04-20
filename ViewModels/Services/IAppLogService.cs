using System.Collections.ObjectModel;

namespace KafkaLens.ViewModels.Services;

public interface IAppLogService
{
    ObservableCollection<AppLogEntry> Entries { get; }
    AppLogEntry? LatestEntry { get; }

    void LogInfo(string message, string? source = null);
    void LogWarning(string message, string? source = null);
    void LogError(string message, string? source = null);
    void Clear();
}
