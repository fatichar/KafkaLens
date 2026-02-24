using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using KafkaLens.Shared;
using Serilog;

namespace KafkaLens.ViewModels.Services;

public class UpdateService : IUpdateService
{
    private const string REPO_OWNER = "fatichar";
    private const string REPO_NAME = "KafkaLens";
    private readonly HttpClient httpClient;

    public UpdateService()
    {
        httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KafkaLens", "1.0"));
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases/latest";
            var response = await httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? "";
            var latestVersionStr = tagName.TrimStart('v');
            if (!Version.TryParse(latestVersionStr, out var latestVersion))
            {
                return new UpdateCheckResult(false, null, null, null, null, null);
            }

            var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);

            if (latestVersion <= currentVersion && currentVersion != new Version(0, 0))
            {
                return new UpdateCheckResult(false, tagName, null, null, null, null);
            }

            var releaseNotes = root.GetProperty("body").GetString();
            var assets = root.GetProperty("assets").EnumerateArray();

            string? downloadUrl = null;
            string? checksumUrl = null;
            string? assetName = null;

            foreach (var asset in assets)
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name == "checksums.txt")
                {
                    checksumUrl = asset.GetProperty("browser_download_url").GetString();
                }
                else if (IsForThisPlatform(name))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    assetName = name;
                }
            }

            return new UpdateCheckResult(
                downloadUrl != null,
                tagName,
                releaseNotes,
                downloadUrl,
                checksumUrl,
                assetName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check for updates");
            return new UpdateCheckResult(false, null, null, null, null, null);
        }
    }

    private bool IsForThisPlatform(string name)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return name.Contains("win-x64", StringComparison.CurrentCultureIgnoreCase);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? name.Contains("macos-arm64", StringComparison.CurrentCultureIgnoreCase)
                : name.Contains("macos-x64", StringComparison.CurrentCultureIgnoreCase);
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return name.Contains("linux-x64", StringComparison.CurrentCultureIgnoreCase);
        }
        return false;
    }

    public bool IsInstallDirectoryWritable()
    {
        try
        {
            var appDir = AppContext.BaseDirectory;
            var testFile = Path.Combine(appDir, ".write_test");
            File.WriteAllText(testFile, "test");
            File.Delete(testFile);
            return true;
        }
        catch
        {
            return false;
        }
    }
}