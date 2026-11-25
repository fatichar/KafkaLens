using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;

namespace KafkaLens.Shared.Security;

/// <summary>
/// Freedesktop Secret Service (GNOME Keyring, KWallet, KeePassXC…) through the <c>secret-tool</c>
/// command from libsecret. Secrets are passed on stdin/stdout only, never on the command line.
/// </summary>
[SupportedOSPlatform("linux")]
public sealed class LinuxSecretServiceStore : ISecretStore
{
    private const string Tool = "secret-tool";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly string toolPath;

    private LinuxSecretServiceStore(string toolPath) => this.toolPath = toolPath;

    public string BackendName => "Secret Service (libsecret)";
    public bool IsPersistent => true;

    /// <summary>
    /// Returns a store when <c>secret-tool</c> is installed and a Secret Service provider answers on the
    /// session bus; otherwise null so the caller can fall back.
    /// </summary>
    public static LinuxSecretServiceStore? TryCreate()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS")))
            return null;
        var path = FindOnPath(Tool);
        if (path == null) return null;

        var store = new LinuxSecretServiceStore(path);
        try
        {
            // A harmless search; fails when no Secret Service provider is running.
            var result = store.Run(null, "search", "service", SecretKeys.ServiceName, "key", "__kafkalens_probe__");
            // No match exits 0/1 silently; a missing/unreachable provider reports an error on stderr.
            return result.ExitCode is 0 or 1 && string.IsNullOrWhiteSpace(result.StdErr) ? store : null;
        }
        catch
        {
            return null;
        }
    }

    public string? Get(string key)
    {
        var result = Run(null, "lookup", "service", SecretKeys.ServiceName, "key", key);
        if (result.ExitCode != 0) return null; // secret-tool exits 1 when nothing matches
        return result.StdOut;
    }

    public void Set(string key, string secret)
    {
        var result = Run(secret, "store", $"--label={SecretKeys.ServiceName}: {key}", "service", SecretKeys.ServiceName, "key", key);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"Could not store the secret in the Secret Service: {result.StdErr.Trim()}");
    }

    public void Delete(string key)
    {
        // Exit code 1 means "nothing to delete".
        Run(null, "clear", "service", SecretKeys.ServiceName, "key", key);
    }

    private (int ExitCode, string StdOut, string StdErr) Run(string? stdin, params string[] args)
    {
        var psi = new ProcessStartInfo(toolPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Could not start secret-tool.");
        if (stdin != null) process.StandardInput.Write(stdin);
        process.StandardInput.Close();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(Timeout))
        {
            try { process.Kill(); } catch { /* already gone */ }
            throw new TimeoutException("secret-tool did not respond (is the keyring locked?).");
        }
        return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static string? FindOnPath(string file) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(dir => Path.Combine(dir, file))
        .FirstOrDefault(File.Exists);
}
