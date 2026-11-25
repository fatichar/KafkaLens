using System;
using Serilog;

namespace KafkaLens.Shared.Security;

public static class SecretStoreFactory
{
    /// <summary>
    /// The OS keyring for the current platform, or the session-only <see cref="InMemorySecretStore"/>
    /// when none is available (secrets are then never written to disk).
    /// </summary>
    public static ISecretStore CreateDefault()
    {
        ISecretStore? store = null;
        try
        {
            if (OperatingSystem.IsWindows()) store = new WindowsCredentialSecretStore();
            else if (OperatingSystem.IsMacOS()) store = new MacKeychainSecretStore();
            else if (OperatingSystem.IsLinux()) store = LinuxSecretServiceStore.TryCreate();
        }
        catch (Exception e)
        {
            Log.Warning(e, "OS credential storage could not be initialised");
        }

        if (store != null)
        {
            Log.Information("Using {SecretStore} for credentials", store.BackendName);
            return store;
        }

        Log.Warning("No OS credential storage available; credentials will be kept in memory for this session only " +
                    "and must be re-entered after a restart");
        return new InMemorySecretStore();
    }

    /// <summary>
    /// For headless servers: environment variables (<see cref="EnvironmentSecretStore"/>) take precedence,
    /// then the OS keyring / in-memory fallback.
    /// </summary>
    public static ISecretStore CreateForServer() => new ChainedSecretStore(new EnvironmentSecretStore(), CreateDefault());
}
