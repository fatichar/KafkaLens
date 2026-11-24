using System;
using System.IO;

namespace KafkaLens.Updater;

internal static class ArchiveUpdate
{
    internal static void ReplaceFiles(string sourcePath, string destinationPath, string executableName)
    {
        var source = new DirectoryInfo(sourcePath);
        if (!File.Exists(Path.Combine(source.FullName, executableName)))
        {
            // Release archives contain a single versioned directory. Also accept older flat archives.
            var entries = source.GetFileSystemInfos();
            if (entries.Length != 1 || entries[0] is not DirectoryInfo package ||
                !File.Exists(Path.Combine(package.FullName, executableName)))
            {
                throw new InvalidDataException($"The update archive is missing {executableName}.");
            }
            source = package;
        }

        CopyDirectory(source, Path.GetFullPath(destinationPath));
    }

    private static void CopyDirectory(DirectoryInfo source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var directory in source.EnumerateDirectories())
        {
            if (!directory.Name.Equals("updater", StringComparison.OrdinalIgnoreCase))
            {
                CopyDirectory(directory, Path.Combine(destination, directory.Name));
            }
        }

        foreach (var file in source.EnumerateFiles())
        {
            var target = Path.Combine(destination, file.Name);
            // Rename within the destination directory so Linux can replace binaries still in use.
            var temporaryFile = Path.Combine(destination, $".kafkalens-update-{Guid.NewGuid():N}");
            try
            {
                file.CopyTo(temporaryFile);
                File.Move(temporaryFile, target, overwrite: true);
            }
            finally
            {
                File.Delete(temporaryFile);
            }
        }
    }
}
