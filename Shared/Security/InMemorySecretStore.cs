using System.Collections.Concurrent;

namespace KafkaLens.Shared.Security;

/// <summary>
/// Session-only fallback used when no OS keyring is available. Secrets are never written to disk;
/// users have to re-enter them after a restart.
/// </summary>
public sealed class InMemorySecretStore : ISecretStore
{
    private readonly ConcurrentDictionary<string, string> secrets = new();

    public string BackendName => "Session memory (not persisted)";
    public bool IsPersistent => false;

    public string? Get(string key) => secrets.TryGetValue(key, out var value) ? value : null;
    public void Set(string key, string secret) => secrets[key] = secret;
    public void Delete(string key) => secrets.TryRemove(key, out _);
}
