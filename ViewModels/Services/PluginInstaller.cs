using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using KafkaLens.Shared.Plugins;
using KafkaLens.ViewModels.Config;
using Serilog;

namespace KafkaLens.ViewModels.Services;

public class PluginInstaller
{
    private readonly string _pluginsDir;
    private readonly HttpClient _http;
    private readonly ISettingsService? _settings;

    public PluginInstaller(string pluginsDir, ISettingsService? settings = null)
    {
        _pluginsDir = pluginsDir;
        _settings = settings;
        _http = new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KafkaLens", "1.0"));
    }

    /// <summary>
    /// Downloads and installs a plugin.
    /// If <see cref="RepositoryPlugin.DownloadUrl"/> ends with <c>.zip</c> the archive is
    /// extracted into <c>plugins/{id}/</c>; otherwise the DLL is placed into
    /// <c>plugins/{id}/plugin.dll</c> for backward compatibility.
    /// </summary>
    public async Task InstallAsync(RepositoryPlugin plugin, CancellationToken ct = default)
    {
        Log.Information("Installing plugin {Id} from {Url}", plugin.Id, plugin.DownloadUrl);

        var bytes = await _http.GetByteArrayAsync(plugin.DownloadUrl, ct);

        if (!string.IsNullOrWhiteSpace(plugin.Sha256))
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes));
            if (!hash.Equals(plugin.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Checksum mismatch for plugin '{plugin.Id}'. " +
                    $"Expected {plugin.Sha256}, got {hash}.");
            }
        }

        var pluginFolder = Path.Combine(_pluginsDir, plugin.Id);
        Directory.CreateDirectory(pluginFolder);

        var url = plugin.DownloadUrl;
        if (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            // Extract ZIP into the plugin folder, overwriting existing files
            using var ms = new MemoryStream(bytes);
            using var zip = new ZipArchive(ms, ZipArchiveMode.Read, leaveOpen: false);
            foreach (var entry in zip.Entries)
            {
                // Skip directory entries and macOS metadata
                if (string.IsNullOrEmpty(entry.Name) || entry.Name.StartsWith("__MACOSX/")) continue;

                // Use the full path from the zip entry to preserve directory structure
                var destPath = Path.Combine(pluginFolder, entry.FullName);
                
                // Ensure the directory exists for the file
                var destDir = Path.GetDirectoryName(destPath);
                if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
                {
                    Directory.CreateDirectory(destDir);
                }
                
                entry.ExtractToFile(destPath, overwrite: true);
            }
            Log.Information("Plugin {Id} installed (ZIP) to {Folder}", plugin.Id, pluginFolder);
        }
        else
        {
            // Legacy / bare DLL: place as plugin.dll inside the folder
            var destPath = Path.Combine(pluginFolder, "plugin.dll");
            await File.WriteAllBytesAsync(destPath, bytes, ct);
            Log.Information("Plugin {Id} installed (DLL) to {Path}", plugin.Id, destPath);
        }

        // Mark plugin as enabled in settings
        if (_settings != null)
        {
            var pluginSettings = _settings.GetPluginSettings();
            pluginSettings.PluginStates[plugin.Id] = true;
            _settings.SavePluginSettings(pluginSettings);
            Log.Information("Plugin {Id} marked as enabled in settings", plugin.Id);
        }
    }

    /// <summary>
    /// Removes a plugin.  If <paramref name="folderPath"/> is non-empty the entire
    /// folder is deleted; otherwise just the file at <paramref name="filePath"/> is deleted
    /// (legacy backward-compat).
    /// </summary>
    public void Uninstall(string filePath, string folderPath = "")
    {
        try
        {
            if (!string.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, recursive: true);
                Log.Information("Plugin folder deleted: {Path}", folderPath);
            }
            else if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                File.Delete(filePath);
                Log.Information("Plugin file deleted: {Path}", filePath);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to delete plugin at {Path}",
                string.IsNullOrEmpty(folderPath) ? filePath : folderPath);
            throw;
        }
    }
}
