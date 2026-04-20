using System;
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
