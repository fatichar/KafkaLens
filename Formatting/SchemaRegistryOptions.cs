using System.Security.Cryptography;
using System.Text;
using Confluent.SchemaRegistry;

namespace KafkaLens.Formatting;

/// <summary>How to reach a Confluent-compatible Schema Registry: URL, optional basic auth and CA.</summary>
public sealed record SchemaRegistryOptions(string Url, string? Username = null, string? Password = null, string? CaLocation = null)
{
    public SchemaRegistryConfig ToConfig()
    {
        var config = new SchemaRegistryConfig
        {
            Url = Url.Trim(),
            // Certificates of an HTTPS registry are always verified.
            EnableSslCertificateVerification = true
        };
        if (!string.IsNullOrWhiteSpace(Username))
        {
            config.BasicAuthCredentialsSource = AuthCredentialsSource.UserInfo;
            config.BasicAuthUserInfo = $"{Username}:{Password}";
        }
        if (!string.IsNullOrWhiteSpace(CaLocation))
            config.SslCaLocation = CaLocation;
        return config;
    }

    /// <summary>Distinguishes connections without exposing the password.</summary>
    public string CacheKey()
    {
        var passwordHash = string.IsNullOrEmpty(Password) ? "" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Password)));
        return $"{Url.Trim()}|{Username}|{CaLocation}|{passwordHash}";
    }

    public override string ToString() => Url; // never print credentials
}
