namespace KafkaLens.Shared.Security;

/// <summary>
/// Stores secrets (passwords, API secrets, key passphrases) outside of plain configuration files.
/// Keys are opaque, non-secret identifiers such as <c>cluster/{id}/sasl.password</c>.
/// </summary>
public interface ISecretStore
{
    /// <summary>Human-readable backend name for diagnostics, e.g. "Windows Credential Manager".</summary>
    string BackendName { get; }

    /// <summary>
    /// True when secrets survive an application restart. False for the in-memory fallback used when no
    /// OS keyring is available; callers must then treat secrets as session-only.
    /// </summary>
    bool IsPersistent { get; }

    /// <summary>Returns the secret, or null when it does not exist.</summary>
    string? Get(string key);

    /// <summary>Creates or replaces a secret.</summary>
    void Set(string key, string secret);

    /// <summary>Deletes a secret. Deleting a missing secret is not an error.</summary>
    void Delete(string key);
}

public static class SecretKeys
{
    public const string ServiceName = "KafkaLens";

    public static string ForCluster(string clusterId, string secretName) => $"cluster/{clusterId}/{secretName}";
}
