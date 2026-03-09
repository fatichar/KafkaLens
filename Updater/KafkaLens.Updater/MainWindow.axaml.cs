using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace KafkaLens.Updater;

public partial class MainWindow : Window
{
    private string? downloadUrl;
    private string? checksumUrl;
    private string? assetName;
    private string? destPath;
    private string? executablePath;
    private int pid;

    private readonly HttpClient httpClient = new();
    private readonly CancellationTokenSource cts = new();

    public MainWindow()
    {
        try
        {
            Log.Information("Started KafkaLens Updater - Initializing UI");
            Log.Information("Current working directory: {CurrentDir}", Directory.GetCurrentDirectory());
            
            InitializeComponent();
            Log.Information("UI components initialized");
            
            var args = Environment.GetCommandLineArgs();
            Log.Information("Retrieved command line arguments: {ArgCount} args", args.Length);
            
            ParseArgs(args);
            Log.Information("Arguments parsed successfully");

            Opened += async (s, e) => 
            {
                Log.Information("Window opened, starting update process");
                await StartUpdate();
            };
            
            Log.Information("MainWindow constructor completed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize MainWindow");
            throw;
        }
    }

    #region Argument Parsing
    private void ParseArgs(string[] args)
    {
        Log.Information("Updater started with args: {Args}", string.Join(" ", args));
        Log.Information("Process name: {ProcessName}", Process.GetCurrentProcess().ProcessName);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid":
                    if (i + 1 < args.Length)
                    {
                        if (int.TryParse(args[++i], out pid))
                        {
                            Log.Information("Parsed PID: {Pid}", pid);
                        }
                        else
                        {
                            Log.Warning("Failed to parse PID: {PidValue}", args[i]);
                        }
                    }
                    break;
                case "--url":
                    if (i + 1 < args.Length) downloadUrl = args[++i];
                    break;
                case "--checksum-url":
                    if (i + 1 < args.Length) checksumUrl = args[++i];
                    break;
                case "--asset-name":
                    if (i + 1 < args.Length) assetName = args[++i];
                    break;
                case "--dest":
                    if (i + 1 < args.Length) destPath = args[++i];
                    break;
                case "--executable":
                    if (i + 1 < args.Length) executablePath = args[++i];
                    break;
            }
        }

        Log.Information("Parsed arguments - PID: {Pid}, DownloadUrl: {Url}, DestPath: {Dest}, Executable: {Exec}, AssetName: {Asset}",
            pid, downloadUrl, destPath, executablePath, assetName);
    }
    #endregion

    #region Main Update Logic
    private async Task StartUpdate()
    {
        try
        {
            Log.Information("Starting update process");

            if (string.IsNullOrEmpty(downloadUrl) || string.IsNullOrEmpty(destPath) || string.IsNullOrEmpty(executablePath) || string.IsNullOrEmpty(assetName))
            {
                await HandleError("Missing required arguments. Updater was launched with invalid parameters.");
                return;
            }

            await WaitForMainAppToExit();

            var sessionTempDir = CreateSessionTempDir();
            var archivePath = Path.Combine(sessionTempDir, assetName);
            Log.Information("Created temporary directory: {TempDir}", sessionTempDir);

            await DownloadAndVerifyAsync(archivePath, sessionTempDir);

            Dispatcher.UIThread.Post(() => CancelButton.IsVisible = false);

            var isInstaller = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                              assetName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

            if (isInstaller)
                await PerformInstallerUpdate(archivePath);
            else
                await PerformArchiveUpdate(archivePath, sessionTempDir);

            await FinalizeAndRestart(sessionTempDir);
        }
        catch (OperationCanceledException)
        {
            Log.Information("Update was cancelled by user");
            await UpdateStatus("Update cancelled.", "");
            await Task.Delay(2000);
            Close();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Update failed with exception");
            await HandleError(ex.Message);
        }
    }

    private async Task PerformInstallerUpdate(string archivePath)
    {
        Log.Information("Running installer {InstallerPath}", archivePath);
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await UpdateStatus("Running installer...", "The installer will now launch to complete the update.");
            await UpdateProgress(90);
        });

        try
        {
            // Launch the installer and don't wait for it to complete
            var installProcess = Process.Start(new ProcessStartInfo
            {
                FileName = archivePath,
                UseShellExecute = true,
            });

            if (installProcess != null)
            {
                Log.Information("Installer launched successfully with PID {Pid}", installProcess.Id);
                await UpdateStatus("Installer launched", "Please follow the installer instructions to complete the update.");
                
                // Give user time to see the message
                await Task.Delay(3000);
                
                Log.Information("Updater exiting - installer will handle the rest");
                Environment.Exit(0);
            }
            else
            {
                throw new Exception("Failed to launch installer process");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to launch installer");
            await HandleError($"Failed to launch installer: {ex.Message}");
        }
    }

    private async Task PerformArchiveUpdate(string archivePath, string sessionTempDir)
    {
        await UpdateStatus("Extracting update...", "Reading archive...");
        await UpdateProgress(85);

        var extractDir = Path.Combine(sessionTempDir, "extracted");
        Directory.CreateDirectory(extractDir);

        Log.Information("Extracting archive {Archive} to {ExtractDir}", archivePath, extractDir);
        if (archivePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => ZipFile.ExtractToDirectory(archivePath, extractDir));
        }
        else if (archivePath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
        {
            await ExtractTarGz(archivePath, extractDir);
        }
        else
        {
            throw new Exception("Unsupported archive format: " + Path.GetExtension(archivePath));
        }
        Log.Information("Archive extraction completed");

        await UpdateStatus("Replacing files...", "Backing up and replacing...");
        await UpdateProgress(90);

        Log.Information("Replacing files from {Source} to {Dest}", extractDir, destPath);
        await Task.Run(() => ReplaceFiles(extractDir, destPath!));
        Log.Information("File replacement completed");
    }

    private async Task FinalizeAndRestart(string sessionTempDir)
    {
        await UpdateStatus("Finalizing...", "Removing temporary files...");
        await Task.Delay(2000); // Give user time to read the message

        await UpdateProgress(95);

        RemoveTempDir(sessionTempDir);

        await UpdateStatus("Finished", "Update completed successfully");
        await UpdateProgress(100);
        await Task.Delay(1000);

        await UpdateStatus("Restarting KafkaLens...", "Launching new version...");
        await Task.Delay(1000);

        Log.Information("Launching application {Executable} in {WorkingDir}", executablePath, destPath);
        Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true,
            WorkingDirectory = destPath
        });

        Close();
    }
    #endregion

    #region Update Steps
    private async Task WaitForMainAppToExit()
    {
        if (pid <= 0) return;

        await UpdateStatus("Waiting for KafkaLens to exit...", "");
        Log.Information("Waiting for process {Pid} to exit", pid);
        try
        {
            var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                await process.WaitForExitAsync(cts.Token);
                Log.Information("Process {Pid} has exited", pid);
            }
            else
            {
                Log.Information("Process {Pid} was already exited", pid);
            }
        }
        catch (ArgumentException)
        {
            Log.Warning("Process with PID {Pid} not found, continuing", pid);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Log.Warning(ex, "Could not wait for process {Pid}", pid);
            await UpdateStatus("Warning", "Could not wait for process: " + ex.Message);
        }
    }

    private static string CreateSessionTempDir()
    {
        var appTempDir = Path.Combine(Path.GetTempPath(), "KafkaLensUpdate");
        var sessionTempDir = Path.Combine(appTempDir, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sessionTempDir);
        return sessionTempDir;
    }

    private async Task DownloadAndVerifyAsync(string archivePath, string tempDir)
    {
        await UpdateStatus("Downloading update...", "Connecting...");
        Dispatcher.UIThread.Post(() => CancelButton.IsVisible = true);

        Log.Information("Downloading from {Url} to {Path}", downloadUrl, archivePath);
        await DownloadFileAsync(downloadUrl!, archivePath, (progress) => _ = UpdateProgress(progress * 0.8));
        Log.Information("Download completed");
        await UpdateStatus("Download complete", "Verifying file integrity...");

        if (string.IsNullOrEmpty(checksumUrl))
        {
            Log.Information("No checksum URL provided, skipping verification");
            return;
        }

        Log.Information("Verifying checksum using {ChecksumUrl}", checksumUrl);
        await UpdateStatus("Verifying checksum...", "Fetching checksums.txt...");
        var checksumsFile = Path.Combine(tempDir, "checksums.txt");
        await DownloadFileAsync(checksumUrl, checksumsFile, null);

        var expectedChecksum = await GetExpectedChecksum(checksumsFile, assetName!);
        if (string.IsNullOrEmpty(expectedChecksum))
        {
            throw new Exception($"Checksum for {assetName} not found in checksums file.");
        }

        var actualChecksum = await CalculateSha256(archivePath);
        Log.Information("Checksum verification - Expected: {Expected}, Actual: {Actual}", expectedChecksum, actualChecksum);

        if (!string.Equals(expectedChecksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
        {
            throw new Exception($"Checksum mismatch! Expected: {expectedChecksum}, Actual: {actualChecksum}");
        }
        Log.Information("Checksum verification passed");
    }

    private async Task DownloadFileAsync(string url, string destinationPath, Action<double>? progressCallback)
    {
        using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts.Token);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        var totalRead = 0L;
        while (await contentStream.ReadAsync(buffer, 0, buffer.Length, cts.Token) is { } read and > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read, cts.Token);
            totalRead += read;
            if (totalBytes != -1)
            {
                progressCallback?.Invoke((double)totalRead / totalBytes * 100);
            }
        }
    }
    #endregion

    #region Helpers
    private async Task<string?> GetExpectedChecksum(string checksumsFile, string assetName)
    {
        var lines = await File.ReadAllLinesAsync(checksumsFile, cts.Token);
        return lines
            .Select(line => line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length >= 2)
            .FirstOrDefault(parts => parts.Contains(assetName, StringComparer.OrdinalIgnoreCase))
            ?.FirstOrDefault(part => !part.Equals(assetName, StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<string> CalculateSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        await using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private async Task ExtractTarGz(string archivePath, string destination)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new NotSupportedException("tar.gz extraction is not supported on Windows.");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "tar",
            Arguments = $"-xzf \"{archivePath}\" -C \"{destination}\"",
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var process = Process.Start(startInfo);
        if (process == null) throw new Exception("Failed to start tar process.");

        await process.WaitForExitAsync(cts.Token);
        if (process.ExitCode == 0) return;

        var error = await process.StandardError.ReadToEndAsync(cts.Token);
        throw new Exception($"tar failed with exit code {process.ExitCode}: {error}");
    }

    private static void ReplaceFiles(string sourceDir, string destDir)
    {
        const string updaterDirName = "updater";
        var source = new DirectoryInfo(sourceDir);
        var destination = new DirectoryInfo(destDir);

        foreach (var dir in source.GetDirectories("*", SearchOption.AllDirectories))
        {
            if (dir.Name.Equals(updaterDirName, StringComparison.OrdinalIgnoreCase)) continue;
            var targetDir = Path.Combine(destination.FullName, dir.FullName.Replace(source.FullName, "").TrimStart('\\'));
            Directory.CreateDirectory(targetDir);
        }

        foreach (var file in source.GetFiles("*", SearchOption.AllDirectories))
        {
            if (file.DirectoryName?.Contains(updaterDirName, StringComparison.OrdinalIgnoreCase) == true) continue;
            var targetFile = Path.Combine(destination.FullName, file.FullName.Replace(source.FullName, "").TrimStart('\\'));
            file.CopyTo(targetFile, true);
        }
    }

    private static void RemoveTempDir(string tempDir)
    {
        try
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
                Log.Information("Cleaned up temporary directory: {TempDir}", tempDir);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to clean up temporary directory {TempDir}", tempDir);
        }
    }

    private async Task HandleError(string message)
    {
        await UpdateStatus("Update failed!", message);
        await Task.Delay(10000);
        Close();
    }

    private async Task UpdateStatus(string status, string detail)
    {
        Log.Information(status + ": " + detail);
        await Dispatcher.UIThread.InvokeAsync(() => {
            StatusText.Text = status;
            DetailText.Text = detail;
        });
    }

    private async Task UpdateProgress(double value)
    {
        await Dispatcher.UIThread.InvokeAsync(() => {
            ProgressBar.Value = value;
        });
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        cts.Cancel();
    }
    #endregion
}