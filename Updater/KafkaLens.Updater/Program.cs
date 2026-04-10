using Avalonia;
using System;
using System.IO;
using Serilog;

namespace KafkaLens.Updater;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // Determine log path based on configuration
            var logPath = GetLogPath();
            var logDirectory = Path.GetDirectoryName(logPath);
            
            if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
            {
                try
                {
                    Directory.CreateDirectory(logDirectory);
                }
                catch
                {
                    // Fallback to temp directory if we can't create logs in AppData
                    var tempLogPath = Path.Combine(Path.GetTempPath(), "KafkaLensUpdater", "logs", "updater-.txt");
                    var tempLogDir = Path.GetDirectoryName(tempLogPath);
                    if (!string.IsNullOrEmpty(tempLogDir))
                    {
                        Directory.CreateDirectory(tempLogDir);
                    }
                    logPath = tempLogPath;
                }
            }

            // Configure Serilog for console and file logging
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Console()
                .WriteTo.File(logPath, 
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 7)
                .CreateLogger();

            Log.Information("=== KafkaLens Updater Started ===");
            Log.Information("Arguments: {Args}", string.Join(" ", args));
            Log.Information("Log file path: {LogPath}", logPath);
            Log.Information("Current directory: {CurrentDir}", Directory.GetCurrentDirectory());
            Log.Information("Process ID: {Pid}", Environment.ProcessId);
            
            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Try to log to console if file logging fails
            try
            {
                Console.WriteLine($"FATAL: Updater failed to start: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            catch
            {
                // Last resort - write to temp file
                var errorLog = Path.Combine(Path.GetTempPath(), "KafkaLensUpdater", "error.txt");
                var errorDir = Path.GetDirectoryName(errorLog);
                if (!string.IsNullOrEmpty(errorDir))
                {
                    Directory.CreateDirectory(errorDir);
                }
                File.WriteAllText(errorLog, $"FATAL: {ex.Message}\n{ex.StackTrace}");
            }
        }
        finally
        {
            try
            {
                Log.CloseAndFlush();
            }
            catch
            {
                // Ignore logging cleanup errors
            }
        }
    }

    private static string GetLogPath()
    {
#if RELEASE
        // In Release, use AppData\Local\KafkaLens
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appDataPath, "KafkaLens", "logs", "updater-.txt");
#else
        // In Debug, use local logs directory
        return "logs/updater-.txt";
#endif
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .LogToTrace();
}
