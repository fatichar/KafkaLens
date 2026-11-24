using System.Linq;

namespace KafkaLens.Shared.Security;

/// <summary>
/// Reads from a read-only store first (e.g. environment variables) and falls back to a writable store,
/// which receives all writes.
/// </summary>
public sealed class ChainedSecretStore(ISecretStore readFirst, ISecretStore writable) : ISecretStore
{
    public string BackendName => $"{readFirst.BackendName}, then {writable.BackendName}";
    public bool IsPersistent => writable.IsPersistent;

    public string? Get(string key) => readFirst.Get(key) ?? writable.Get(key);
    public void Set(string key, string secret) => writable.Set(key, secret);
    public void Delete(string key) => writable.Delete(key);
}
