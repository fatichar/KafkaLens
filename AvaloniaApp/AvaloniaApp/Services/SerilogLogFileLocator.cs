using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace AvaloniaApp.Services;

public sealed class SerilogLogFileLocator
{
    private readonly string configuredLogPath;

    public SerilogLogFileLocator(string configuredLogPath)
    {
        this.configuredLogPath = configuredLogPath;
    }

    public string? ArchiveActiveLogFile(DateTime launchTime)
    {
        if (!File.Exists(configuredLogPath))
            return null;

        var directory = Path.GetDirectoryName(configuredLogPath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = Directory.GetCurrentDirectory();

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(configuredLogPath);
        var extension = Path.GetExtension(configuredLogPath);
        var timestamp = launchTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var archivePath = Path.Combine(directory, $"{fileNameWithoutExtension}-{timestamp}{extension}");
        var suffix = 1;

        while (File.Exists(archivePath))
        {
            archivePath = Path.Combine(directory, $"{fileNameWithoutExtension}-{timestamp}-{suffix}{extension}");
            suffix++;
        }

        File.Move(configuredLogPath, archivePath);
        return archivePath;
    }

    public string? FindLatestLogFile()
    {
        var directory = Path.GetDirectoryName(configuredLogPath);
        if (string.IsNullOrWhiteSpace(directory))
            directory = Directory.GetCurrentDirectory();

        if (!Directory.Exists(directory))
            return null;

        var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(configuredLogPath);
        var extension = Path.GetExtension(configuredLogPath);
        var searchPattern = string.IsNullOrWhiteSpace(fileNameWithoutExtension)
            ? $"*{extension}"
            : $"{fileNameWithoutExtension}*{extension}";

        return Directory
            .EnumerateFiles(directory, searchPattern)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }
}
