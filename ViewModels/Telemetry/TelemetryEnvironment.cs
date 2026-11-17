using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace KafkaLens.ViewModels.Telemetry;

/// <summary>Static facts about the running app/OS. No user or machine identifiers.</summary>
public sealed record TelemetryEnvironment(
    string AppVersion,
    string Os,
    string OsVersion,
    string Arch,
    string Runtime,
    string InstallKind,
    string Locale)
{
    public static TelemetryEnvironment Detect()
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version;
        var os = OperatingSystem.IsWindows() ? "windows"
            : OperatingSystem.IsMacOS() ? "macos"
            : OperatingSystem.IsLinux() ? "linux"
            : OperatingSystem.IsAndroid() ? "android"
            : OperatingSystem.IsBrowser() ? "browser"
            : "other";
        var osVersion = Environment.OSVersion.Version;
        return new TelemetryEnvironment(
            version?.ToString(3) ?? "0.0.0",
            os,
            $"{osVersion.Major}.{osVersion.Minor}.{Math.Max(osVersion.Build, 0)}",
            RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            Environment.Version.ToString(2),
            DetectInstallKind(os),
            CultureInfo.CurrentUICulture.Name is { Length: > 0 } name ? name : "invariant");
    }

    /// <summary>Browser-like UA so Umami parses the OS and doesn't treat the client as a bot.</summary>
    public string UserAgent => Os switch
    {
        "windows" => $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) KafkaLens/{AppVersion}",
        "macos" => $"Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) KafkaLens/{AppVersion}",
        "linux" => $"Mozilla/5.0 (X11; Linux x86_64) KafkaLens/{AppVersion}",
        _ => $"Mozilla/5.0 KafkaLens/{AppVersion}"
    };

    private static string DetectInstallKind(string os)
    {
        try
        {
            var baseDir = AppContext.BaseDirectory;
            if (os == "windows")
            {
                if (baseDir.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase)) return "msix";
                var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                var localPrograms = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs");
                if ((!string.IsNullOrEmpty(programFiles) && baseDir.StartsWith(programFiles, StringComparison.OrdinalIgnoreCase)) ||
                    baseDir.StartsWith(localPrograms, StringComparison.OrdinalIgnoreCase))
                    return "installer";
                return "zip";
            }
            if (os == "macos" && baseDir.Contains(".app/", StringComparison.Ordinal)) return "app_bundle";
            return "zip";
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
