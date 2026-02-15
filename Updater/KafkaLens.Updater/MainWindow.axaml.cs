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

namespace KafkaLens.Updater;

public partial class MainWindow : Window
{
    private string? _downloadUrl;
    private string? _checksumUrl;
    private string? _assetName;
    private string? _destPath;
    private string? _executablePath;
    private int _pid;

    private readonly HttpClient _httpClient = new();
    private readonly CancellationTokenSource _cts = new();

    public MainWindow()
    {
        InitializeComponent();

        var args = Environment.GetCommandLineArgs();
        ParseArgs(args);

        Opened += async (s, e) => await StartUpdate();
    }

    private void ParseArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pid":
                    if (i + 1 < args.Length) int.TryParse(args[++i], out _pid);
                    break;
                case "--url":
                    if (i + 1 < args.Length) _downloadUrl = args[++i];
                    break;
                case "--checksum-url":
                    if (i + 1 < args.Length) _checksumUrl = args[++i];
                    break;
                case "--asset-name":
                    if (i + 1 < args.Length) _assetName = args[++i];
                    break;
                case "--dest":
                    if (i + 1 < args.Length) _destPath = args[++i];
                    break;
                case "--executable":
                    if (i + 1 < args.Length) _executablePath = args[++i];
                    break;
            }
        }
    }

    private async Task StartUpdate()
    {
        try
        {
            if (string.IsNullOrEmpty(_downloadUrl) || string.IsNullOrEmpty(_destPath) || string.IsNullOrEmpty(_executablePath))
            {
                UpdateStatus("Error: Missing arguments.", "Updater was launched with invalid parameters.");
                await Task.Delay(5000);
                Close();
                return;
            }

            if (_pid > 0)
            {
                UpdateStatus("Waiting for KafkaLens to exit...", "");
                try
                {
                    var process = Process.GetProcessById(_pid);
                    if (!process.HasExited)
                    {
                        await process.WaitForExitAsync(_cts.Token);
                    }
                }
                catch (ArgumentException) { }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    UpdateStatus("Warning", "Could not wait for process: " + ex.Message);
                }
            }

            var tempDir = Path.Combine(Path.GetTempPath(), "KafkaLensUpdate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var archivePath = Path.Combine(tempDir, _assetName ?? "update.archive");

            UpdateStatus("Downloading update...", "Connecting...");
            Dispatcher.UIThread.Post(() => CancelButton.IsVisible = true);

            await DownloadFileAsync(_downloadUrl, archivePath, (progress) => UpdateProgress(progress * 0.8));

            if (!string.IsNullOrEmpty(_checksumUrl))
            {
                UpdateStatus("Verifying checksum...", "Fetching checksums.txt...");
                var checksumsFile = Path.Combine(tempDir, "checksums.txt");
                await DownloadFileAsync(_checksumUrl, checksumsFile, null);

                var expectedChecksum = await GetExpectedChecksum(checksumsFile, _assetName!);
                if (string.IsNullOrEmpty(expectedChecksum))
                {
                    throw new Exception("Checksum for " + _assetName + " not found in checksums.txt");
                }

                var actualChecksum = await CalculateSHA256(archivePath);
                if (!string.Equals(expectedChecksum, actualChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new Exception($"Checksum mismatch!\nExpected: {expectedChecksum}\nActual: {actualChecksum}");
                }
            }

            Dispatcher.UIThread.Post(() => CancelButton.IsVisible = false);

            UpdateStatus("Extracting update...", "Reading archive...");
            UpdateProgress(85);

            var extractDir = Path.Combine(tempDir, "extracted");
            Directory.CreateDirectory(extractDir);

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

            UpdateStatus("Replacing files...", "Backing up and replacing...");
            UpdateProgress(90);

            await Task.Run(() => ReplaceFiles(extractDir, _destPath));

            UpdateStatus("Finalizing...", "Cleaning up...");
            UpdateProgress(95);

            try { Directory.Delete(tempDir, true); } catch { }

            UpdateStatus("Restarting KafkaLens...", "Launching new version...");
            UpdateProgress(100);
            await Task.Delay(1000);

            Process.Start(new ProcessStartInfo
            {
                FileName = _executablePath,
                UseShellExecute = true,
                WorkingDirectory = _destPath
            });

            Close();
        }
        catch (OperationCanceledException)
        {
            UpdateStatus("Update cancelled.", "");
            await Task.Delay(2000);
            Close();
        }
        catch (Exception ex)
        {
            UpdateStatus("Update failed!", ex.Message);
            await Task.Delay(10000);
            Close();
        }
    }

    private async Task DownloadFileAsync(string url, string destinationPath, Action<double>? progressCallback)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, _cts.Token);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
        using var contentStream = await response.Content.ReadAsStreamAsync(_cts.Token);
        using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        var buffer = new byte[8192];
        var totalRead = 0L;
        int read;
        while ((read = await contentStream.ReadAsync(buffer, 0, buffer.Length, _cts.Token)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, read, _cts.Token);
            totalRead += read;
            if (totalBytes != -1 && progressCallback != null)
            {
                progressCallback((double)totalRead / totalBytes * 100);
            }
        }
    }

    private async Task<string?> GetExpectedChecksum(string checksumsFile, string assetName)
    {
        var lines = await File.ReadAllLinesAsync(checksumsFile);
        foreach (var line in lines)
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                if (parts[1] == assetName) return parts[0];
                if (parts[0] == assetName) return parts[1];
            }
        }
        return null;
    }

    private async Task<string> CalculateSHA256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var hash = await sha256.ComputeHashAsync(stream);
        return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private async Task ExtractTarGz(string archivePath, string destination)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "tar",
                Arguments = $"-xzf \"{archivePath}\" -C \"{destination}\"",
                RedirectStandardError = true,
                UseShellExecute = false
            };
            var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync(_cts.Token);
                if (process.ExitCode != 0)
                {
                    var error = await process.StandardError.ReadToEndAsync();
                    throw new Exception($"tar failed with exit code {process.ExitCode}: {error}");
                }
                return;
            }
        }

        throw new Exception("tar.gz extraction is only supported on Linux/macOS via 'tar' command.");
    }

    private void ReplaceFiles(string sourceDir, string destDir)
    {
        var updaterDirName = "updater";

        foreach (var dirPath in Directory.GetDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, dirPath);
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            if (parts.Contains(updaterDirName))
                continue;

            Directory.CreateDirectory(Path.Combine(destDir, relativePath));
        }

        foreach (var filePath in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDir, filePath);
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            if (parts.Contains(updaterDirName))
                continue;

            var destFile = Path.Combine(destDir, relativePath);
            File.Copy(filePath, destFile, true);
        }
    }

    private void UpdateStatus(string status, string detail)
    {
        Dispatcher.UIThread.Post(() => {
            StatusText.Text = status;
            DetailText.Text = detail;
        });
    }

    private void UpdateProgress(double value)
    {
        Dispatcher.UIThread.Post(() => {
            ProgressBar.Value = value;
        });
    }

    private void Cancel_OnClick(object? sender, RoutedEventArgs e)
    {
        _cts.Cancel();
    }
}
