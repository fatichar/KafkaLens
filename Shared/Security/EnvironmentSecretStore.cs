using System;
using System.Text;

namespace KafkaLens.Shared.Security;

/// <summary>
/// Read-only store resolving secrets from environment variables, intended for headless server
/// deployments (e.g. the gRPC backend in a container) where no keyring exists.
/// Key <c>cluster/prod/sasl.password</c> maps to <c>KAFKALENS_SECRET__CLUSTER__PROD__SASL_PASSWORD</c>.
/// </summary>
public sealed class EnvironmentSecretStore : ISecretStore
{
    public const string Prefix = "KAFKALENS_SECRET__";

    private readonly Func<string, string?> getVariable;

    public EnvironmentSecretStore() : this(Environment.GetEnvironmentVariable) { }

    public EnvironmentSecretStore(Func<string, string?> getVariable) => this.getVariable = getVariable;

    public string BackendName => "Environment variables";
    public bool IsPersistent => true;

    public static string VariableName(string key)
    {
        var sb = new StringBuilder(Prefix);
        foreach (var c in key)
        {
            if (c == '/') sb.Append("__");
            else if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
            else sb.Append('_');
        }
        return sb.ToString();
    }

    public string? Get(string key)
    {
        var value = getVariable(VariableName(key));
        return string.IsNullOrEmpty(value) ? null : value;
    }

    public void Set(string key, string secret) =>
        throw new NotSupportedException("Environment variable secrets are read-only.");

    public void Delete(string key) { }
}
