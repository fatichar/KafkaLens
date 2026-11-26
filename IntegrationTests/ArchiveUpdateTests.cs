using System.IO.Compression;
using Avalonia.Headless.XUnit;
using FluentAssertions;
using KafkaLens.Updater;

namespace IntegrationTests;

public sealed class ArchiveUpdateTests : IDisposable
{
    private readonly DirectoryInfo workspace = Directory.CreateTempSubdirectory("kafkalens-archive-update-");

    [AvaloniaFact]
    public void ReplaceFiles_InstallsFlatArchive() => VerifyInstallation(wrapped: false);

    [AvaloniaFact]
    public void ReplaceFiles_InstallsWrappedArchive() => VerifyInstallation(wrapped: true);

    private void VerifyInstallation(bool wrapped)
    {
        var package = workspace.CreateSubdirectory("KafkaLens-1.2.3-linux-x64");
        var executable = Path.Combine(package.FullName, "KafkaLens");
        File.WriteAllText(executable, "new release");
        if (OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(executable, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
        File.WriteAllText(Path.Combine(package.CreateSubdirectory("resources").FullName, "sample"), "new resource");
        File.WriteAllText(Path.Combine(package.CreateSubdirectory("resources/updater/private").FullName, "skip"), "skip");
        File.WriteAllText(Path.Combine(package.CreateSubdirectory("updater-data").FullName, "keep"), "keep");

        var archive = Path.Combine(workspace.FullName, "release.zip");
        ZipFile.CreateFromDirectory(package.FullName, archive, CompressionLevel.NoCompression, wrapped);
        var extraction = workspace.CreateSubdirectory("extracted");
        ZipFile.ExtractToDirectory(archive, extraction.FullName);
        var install = workspace.CreateSubdirectory("existing-updater-install");
        var installedExecutable = Path.Combine(install.FullName, "KafkaLens");
        File.WriteAllText(installedExecutable, "old release");

        ArchiveUpdate.ReplaceFiles(extraction.FullName, install.FullName, "KafkaLens");

        File.ReadAllText(installedExecutable).Should().Be("new release");
        File.ReadAllText(Path.Combine(install.FullName, "resources", "sample")).Should().Be("new resource");
        Directory.Exists(Path.Combine(install.FullName, "resources", "updater")).Should().BeFalse();
        File.ReadAllText(Path.Combine(install.FullName, "updater-data", "keep")).Should().Be("keep");
        Directory.Exists(Path.Combine(install.FullName, package.Name)).Should().BeFalse();
        install.GetFiles(".kafkalens-update-*", SearchOption.AllDirectories).Should().BeEmpty();
        if (OperatingSystem.IsLinux())
        {
            File.GetUnixFileMode(installedExecutable).Should().HaveFlag(UnixFileMode.UserExecute);
        }
    }

    [AvaloniaFact]
    public void ReplaceFiles_RejectsMissingExecutableBeforeChangingInstallation()
    {
        var package = workspace.CreateSubdirectory("package");
        File.WriteAllText(Path.Combine(package.FullName, "unrelated"), "unrelated");
        var install = workspace.CreateSubdirectory("installed");
        var installedExecutable = Path.Combine(install.FullName, "KafkaLens");
        File.WriteAllText(installedExecutable, "old release");

        var update = () => ArchiveUpdate.ReplaceFiles(package.FullName, install.FullName, "KafkaLens");
        update.Should().Throw<InvalidDataException>();

        File.ReadAllText(installedExecutable).Should().Be("old release");
        install.GetFiles().Should().ContainSingle();
    }

    public void Dispose()
    {
        workspace.Delete(recursive: true);
    }
}
