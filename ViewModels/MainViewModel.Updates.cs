using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using KafkaLens.Shared;
using Serilog;

namespace KafkaLens.ViewModels;

public partial class MainViewModel
{
    public async Task CheckForUpdatesAsync(bool silent)
    {
        Log.Information("Checking for updates...");
        var result = await UpdateService.CheckForUpdateAsync();
        if (result.UpdateAvailable)
        {

            Log.Information("Update available: {Version}", result.LatestVersion);
            var updateVm = new UpdateViewModel(result);
            updateVm.OnUpdate += () => PerformUpdate(result);
            ShowUpdateDialog(updateVm);
        }
        else if (!silent)
        {
            Log.Information("No updates available.");
            // the space at the end is intentional. Without that the last word is not displayed.
            ShowMessage("Update Check", "You are already using the latest version of KafkaLens! ");
        }
    }

    private void PerformUpdate(UpdateCheckResult result)
    {
        if (!ValidateResult(result)) return;

        var appDir = AppContext.BaseDirectory;
        var updaterPath = Path.Combine(appDir, "KafkaLens.Updater");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) updaterPath += ".exe";

        Log.Information("Looking for updater at: {UpdaterPath}", updaterPath);

        if (!File.Exists(updaterPath))
        {
            Log.Error("Updater not found at {UpdaterPath}", updaterPath);
            var currentDirUpdater = Path.Combine(Directory.GetCurrentDirectory(), "KafkaLens.Updater.exe");
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && File.Exists(currentDirUpdater))
            {
                Log.Information("Found updater in current directory: {CurrentDirUpdater}", currentDirUpdater);
                updaterPath = currentDirUpdater;
            }
            else
            {
                Log.Error("Updater not found in current directory either: {CurrentDirUpdater}", currentDirUpdater);
                return;
            }
        }

        var executablePath = Process.GetCurrentProcess().MainModule?.FileName!;
        var processStartInfo = new ProcessStartInfo
        {
            FileName = updaterPath,
            UseShellExecute = false
        };
        
        processStartInfo.ArgumentList.Add("--pid");
        processStartInfo.ArgumentList.Add(Environment.ProcessId.ToString());
        processStartInfo.ArgumentList.Add("--url");
        processStartInfo.ArgumentList.Add(result.DownloadUrl);
        if (result.ChecksumUrl != null)
        {
            processStartInfo.ArgumentList.Add("--checksum-url");
            processStartInfo.ArgumentList.Add(result.ChecksumUrl);
        }
        processStartInfo.ArgumentList.Add("--asset-name");
        processStartInfo.ArgumentList.Add(result.AssetName);
        processStartInfo.ArgumentList.Add("--dest");
        processStartInfo.ArgumentList.Add(appDir);
        processStartInfo.ArgumentList.Add("--executable");
        processStartInfo.ArgumentList.Add(executablePath);

        Log.Information("Starting updater: {UpdaterPath} with {ArgCount} arguments", updaterPath, processStartInfo.ArgumentList.Count);

        try
        {
            var process = Process.Start(processStartInfo);
            if (process is { HasExited: false })
            {
                Log.Information("Updater running (PID {Pid}), exiting main application", process.Id);
                Environment.Exit(0);
            }
            else
            {
                Log.Warning("Updater process exited immediately or failed to start");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start updater process");
            throw;
        }
    }

    private bool ValidateResult(UpdateCheckResult result)
    {
        if (result.DownloadUrl == null) { Log.Error("Download URL is not set"); return false; }
        if (result.AssetName == null) { Log.Error("Asset name is not set"); return false; }
        return true;
    }
}