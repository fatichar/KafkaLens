using System;
using System.Threading.Tasks;

namespace KafkaLens.Shared;

public interface IUpdateService
{
    Task<UpdateCheckResult> CheckForUpdateAsync();
    bool IsInstallDirectoryWritable();
}

public record UpdateCheckResult(
    bool UpdateAvailable,
    string? LatestVersion,
    string? ReleaseNotes,
    string? DownloadUrl,
    string? ChecksumUrl,
    string? AssetName
);
