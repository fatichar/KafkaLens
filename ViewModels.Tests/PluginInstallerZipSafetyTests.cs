using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using KafkaLens.ViewModels.Services;
using Xunit;

namespace KafkaLens.ViewModels.Tests;

/// <summary>
/// Tests for the ZIP extraction logic in <see cref="PluginInstaller.ExtractZip"/>,
/// specifically the Zip Slip path-traversal defence.
/// </summary>
public class PluginInstallerZipSafetyTests : IDisposable
{
    private readonly string _tempRoot;

    public PluginInstallerZipSafetyTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "KafkaLensPluginTests_" + Path.GetRandomFileName());
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Creates an in-memory ZIP and returns its bytes.</summary>
    private static byte[] CreateZip(Action<ZipArchive> populate)
    {
        using var ms  = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            populate(zip);
        return ms.ToArray();
    }

    private static void AddEntry(ZipArchive zip, string entryName, string content = "content")
    {
        var entry = zip.CreateEntry(entryName);
        using var sw = new StreamWriter(entry.Open());
        sw.Write(content);
    }

    // ── Normal extraction ─────────────────────────────────────────────────────

    [Fact]
    public void NormalZip_ExtractsFilesToPluginFolder()
    {
        var pluginFolder = Path.Combine(_tempRoot, "myplugin");
        Directory.CreateDirectory(pluginFolder);

        var bytes = CreateZip(z =>
        {
            AddEntry(z, "plugin.dll", "dll content");
            AddEntry(z, "sub/helper.dll", "helper content");
        });

        PluginInstaller.ExtractZip(bytes, pluginFolder);

        File.Exists(Path.Combine(pluginFolder, "plugin.dll")).Should().BeTrue();
        File.Exists(Path.Combine(pluginFolder, "sub", "helper.dll")).Should().BeTrue();
        File.ReadAllText(Path.Combine(pluginFolder, "plugin.dll")).Should().Be("dll content");
    }

    [Fact]
    public void DirectoryEntries_AreSkipped()
    {
        var pluginFolder = Path.Combine(_tempRoot, "dirplugin");
        Directory.CreateDirectory(pluginFolder);

        var bytes = CreateZip(z =>
        {
            // Entry with empty Name but non-empty FullName is a directory entry
            z.CreateEntry("emptydir/");
            AddEntry(z, "actual.dll");
        });

        PluginInstaller.ExtractZip(bytes, pluginFolder);

        // "emptydir/" should not create a file
        File.Exists(Path.Combine(pluginFolder, "emptydir")).Should().BeFalse();
        File.Exists(Path.Combine(pluginFolder, "actual.dll")).Should().BeTrue();
    }

    [Fact]
    public void MacOsMetadataEntries_AreSkipped()
    {
        var pluginFolder = Path.Combine(_tempRoot, "macplugin");
        Directory.CreateDirectory(pluginFolder);

        var bytes = CreateZip(z =>
        {
            AddEntry(z, "__MACOSX/._plugin.dll", "junk");
            AddEntry(z, "plugin.dll", "real dll");
        });

        PluginInstaller.ExtractZip(bytes, pluginFolder);

        Directory.Exists(Path.Combine(pluginFolder, "__MACOSX")).Should().BeFalse();
        File.ReadAllText(Path.Combine(pluginFolder, "plugin.dll")).Should().Be("real dll");
    }

    // ── Zip Slip defence ──────────────────────────────────────────────────────

    [Fact]
    public void ZipSlip_RelativePathTraversal_IsRejected()
    {
        var pluginFolder = Path.Combine(_tempRoot, "victimPlugin");
        Directory.CreateDirectory(pluginFolder);

        // Entry with a path like "../../evil.dll" targets a parent directory
        var bytes = CreateZip(z =>
        {
            AddEntry(z, "../../evil.dll", "malicious");
        });

        PluginInstaller.ExtractZip(bytes, pluginFolder);

        // The file must NOT have been written outside the plugin folder
        var sibling = Path.Combine(_tempRoot, "evil.dll");
        File.Exists(sibling).Should().BeFalse("path traversal should have been blocked");
    }

    [Fact]
    public void ZipSlip_AbsolutePath_IsRejected()
    {
        var pluginFolder = Path.Combine(_tempRoot, "absPlugin");
        Directory.CreateDirectory(pluginFolder);

        var target = Path.Combine(_tempRoot, "shouldNotExist.dll");
        var bytes   = CreateZip(z =>
        {
            // On Windows, ZipArchive allows absolute paths in FullName
            AddEntry(z, target, "malicious");
        });

        PluginInstaller.ExtractZip(bytes, pluginFolder);

        File.Exists(target).Should().BeFalse("absolute path in ZIP entry should be rejected");
    }

    [Fact]
    public void ZipSlip_MixedGoodAndBadEntries_OnlyGoodAreExtracted()
    {
        var pluginFolder = Path.Combine(_tempRoot, "mixedPlugin");
        Directory.CreateDirectory(pluginFolder);

        var bytes = CreateZip(z =>
        {
            AddEntry(z, "good.dll", "good");
            AddEntry(z, "../../evil.txt", "evil");
        });

        PluginInstaller.ExtractZip(bytes, pluginFolder);

        File.Exists(Path.Combine(pluginFolder, "good.dll")).Should().BeTrue();
        File.Exists(Path.Combine(_tempRoot, "evil.txt")).Should().BeFalse();
    }
}
