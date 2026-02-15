using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using KafkaLens.Shared;
using Serilog;

namespace KafkaLens.ViewModels.Services;

public class UpdateService : IUpdateService
{
    private const string RepoOwner = "fatichar";
    private const string RepoName = "KafkaLens";
    private readonly HttpClient _httpClient;

    public UpdateService()
    {
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KafkaLens", "1.0"));
    }

    public async Task<UpdateCheckResult> CheckForUpdateAsync()
    {
        try
        {
            var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
            var response = await _httpClient.GetStringAsync(url);
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

            var platformAssetSuffix = GetPlatformAssetSuffix();

            foreach (var asset in assets)
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name == "checksums.txt")
                {
                    checksumUrl = asset.GetProperty("browser_download_url").GetString();
                }
                else if (name.EndsWith(platformAssetSuffix, StringComparison.OrdinalIgnoreCase))
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

    private string GetPlatformAssetSuffix()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "windows-x64.zip";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "macos-arm64.zip"
                : "macos-x64.zip";
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return "linux-x64.tar.gz";
        }
        return "unknown";
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
