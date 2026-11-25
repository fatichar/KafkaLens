using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using KafkaLens.Shared.Entities;

namespace KafkaLens.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter<KafkaSecurityProtocol>))]
public enum KafkaSecurityProtocol
{
    Plaintext,
    Ssl,
    /// <summary>SASL authentication without encryption: credentials can be read on the network.</summary>
    SaslPlaintext,
    /// <summary>SASL authentication over TLS (recommended; Confluent Cloud).</summary>
    SaslSsl
}

[JsonConverter(typeof(JsonStringEnumConverter<KafkaSaslMechanism>))]
public enum KafkaSaslMechanism
{
    Plain,
    ScramSha256,
    ScramSha512
}

/// <summary>
/// Non-secret security settings of a Kafka connection. Secrets (passwords) are kept separately in the
/// credential store, see <see cref="ClusterSecretNames"/>.
/// </summary>
public sealed class ClusterSecurity
{
    [JsonPropertyName("protocol")]
    public KafkaSecurityProtocol Protocol { get; set; } = KafkaSecurityProtocol.Plaintext;

    /// <summary>PEM file with the CA certificate(s) that signed the broker certificates. Empty = OS trust store.</summary>
    [JsonPropertyName("sslCaLocation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SslCaLocation { get; set; }

    /// <summary>Client certificate (PEM) for mutual TLS.</summary>
    [JsonPropertyName("sslCertificateLocation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SslCertificateLocation { get; set; }

    /// <summary>Client private key (PEM) for mutual TLS; its passphrase is the <see cref="ClusterSecretNames.SslKeyPassword"/> secret.</summary>
    [JsonPropertyName("sslKeyLocation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SslKeyLocation { get; set; }

    [JsonPropertyName("saslMechanism")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public KafkaSaslMechanism? SaslMechanism { get; set; }

    /// <summary>SASL user name (API key for Confluent Cloud). The password is the <see cref="ClusterSecretNames.SaslPassword"/> secret.</summary>
    [JsonPropertyName("saslUsername")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SaslUsername { get; set; }

    /// <summary>Schema Registry basic-auth user (API key). Its password is the <see cref="ClusterSecretNames.SchemaRegistryPassword"/> secret.</summary>
    [JsonPropertyName("schemaRegistryUsername")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SchemaRegistryUsername { get; set; }

    /// <summary>PEM CA for an HTTPS Schema Registry with a private CA. Empty = OS trust store.</summary>
    [JsonPropertyName("schemaRegistryCaLocation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SchemaRegistryCaLocation { get; set; }

    [JsonIgnore]
    public bool UsesTls => Protocol is KafkaSecurityProtocol.Ssl or KafkaSecurityProtocol.SaslSsl;

    [JsonIgnore]
    public bool UsesSasl => Protocol is KafkaSecurityProtocol.SaslPlaintext or KafkaSecurityProtocol.SaslSsl;

    /// <summary>True when credentials would travel unencrypted (SASL without TLS). The UI must warn.</summary>
    [JsonIgnore]
    public bool SendsCredentialsUnencrypted => Protocol == KafkaSecurityProtocol.SaslPlaintext;

    [JsonIgnore]
    public KafkaSaslMechanism EffectiveSaslMechanism => SaslMechanism ?? KafkaSaslMechanism.Plain;

    public static string MechanismName(KafkaSaslMechanism mechanism) => mechanism switch
    {
        KafkaSaslMechanism.ScramSha256 => "SCRAM-SHA-256",
        KafkaSaslMechanism.ScramSha512 => "SCRAM-SHA-512",
        _ => "PLAIN"
    };

    [JsonIgnore]
    public bool IsDefault => Protocol == KafkaSecurityProtocol.Plaintext
                             && string.IsNullOrWhiteSpace(SchemaRegistryUsername)
                             && string.IsNullOrWhiteSpace(SchemaRegistryCaLocation);

    public string Summary() => Protocol switch
    {
        KafkaSecurityProtocol.Ssl => string.IsNullOrWhiteSpace(SslCertificateLocation) ? "TLS" : "Mutual TLS",
        KafkaSecurityProtocol.SaslSsl => $"SASL/{MechanismName(EffectiveSaslMechanism)} + TLS",
        KafkaSecurityProtocol.SaslPlaintext => $"SASL/{MechanismName(EffectiveSaslMechanism)} (unencrypted)",
        _ => "Plaintext"
    };

    /// <summary>Secrets that are meaningful for these settings; others can be discarded.</summary>
    public IReadOnlySet<string> ApplicableSecretNames()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (UsesTls && !string.IsNullOrWhiteSpace(SslKeyLocation)) names.Add(ClusterSecretNames.SslKeyPassword);
        if (UsesSasl) names.Add(ClusterSecretNames.SaslPassword);
        if (!string.IsNullOrWhiteSpace(SchemaRegistryUsername)) names.Add(ClusterSecretNames.SchemaRegistryPassword);
        return names;
    }

    /// <summary>Returns a problem description, or null when the settings are usable.</summary>
    public string? Validate()
    {
        if (UsesSasl && string.IsNullOrWhiteSpace(SaslUsername)) return "A SASL user name is required.";
        if (!UsesTls) return null;
        var hasCert = !string.IsNullOrWhiteSpace(SslCertificateLocation);
        var hasKey = !string.IsNullOrWhiteSpace(SslKeyLocation);
        if (hasCert != hasKey) return "A client certificate and its private key must be specified together.";
        return null;
    }

    /// <summary>
    /// Combines stored secrets with newly entered ones (non-empty values win) and drops secrets that do not
    /// apply to <paramref name="security"/>.
    /// </summary>
    public static Dictionary<string, string> MergeSecrets(IReadOnlyDictionary<string, string>? existing,
        IReadOnlyDictionary<string, string>? updates, ClusterSecurity security)
    {
        var applicable = security.ApplicableSecretNames();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in new[] { existing, updates })
        {
            if (source == null) continue;
            foreach (var (name, value) in source)
                if (applicable.Contains(name) && !string.IsNullOrEmpty(value)) result[name] = value;
        }
        return result;
    }

    public ClusterSecurity Clone() => (ClusterSecurity)MemberwiseClone();

    public bool SameAs(ClusterSecurity? other) =>
        other != null && Protocol == other.Protocol
                      && Norm(SslCaLocation) == Norm(other.SslCaLocation)
                      && Norm(SslCertificateLocation) == Norm(other.SslCertificateLocation)
                      && Norm(SslKeyLocation) == Norm(other.SslKeyLocation)
                      && (UsesSasl ? EffectiveSaslMechanism : (KafkaSaslMechanism?)null) == (other.UsesSasl ? other.EffectiveSaslMechanism : null)
                      && Norm(SaslUsername) == Norm(other.SaslUsername)
                      && Norm(SchemaRegistryUsername) == Norm(other.SchemaRegistryUsername)
                      && Norm(SchemaRegistryCaLocation) == Norm(other.SchemaRegistryCaLocation);

    private static string Norm(string? s) => s?.Trim() ?? "";

    /// <summary>Trimmed copy with unused fields cleared, suitable for persisting.</summary>
    public ClusterSecurity Normalized()
    {
        var copy = Clone();
        copy.SslCaLocation = Blank(SslCaLocation);
        copy.SslCertificateLocation = Blank(SslCertificateLocation);
        copy.SslKeyLocation = Blank(SslKeyLocation);
        copy.SaslUsername = Blank(SaslUsername);
        copy.SchemaRegistryUsername = Blank(SchemaRegistryUsername);
        copy.SchemaRegistryCaLocation = Blank(SchemaRegistryCaLocation);
        if (!copy.UsesTls)
            copy.SslCaLocation = copy.SslCertificateLocation = copy.SslKeyLocation = null;
        if (copy.UsesSasl)
            copy.SaslMechanism = copy.EffectiveSaslMechanism;
        else
        {
            copy.SaslMechanism = null;
            copy.SaslUsername = null;
        }
        return copy;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
