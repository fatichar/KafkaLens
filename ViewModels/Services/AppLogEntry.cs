namespace KafkaLens.ViewModels.Services;

public enum AppLogLevel
{
    Info,
    Warning,
    Error
}

public sealed record AppLogEntry(DateTime Timestamp, AppLogLevel Level, string Message, string? Source = null);
