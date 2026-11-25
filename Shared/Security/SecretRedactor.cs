using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace KafkaLens.Shared.Security;

/// <summary>Removes secrets from text before it is logged, displayed as diagnostics or exported.</summary>
public static partial class SecretRedactor
{
    public const string Mask = "********";

    // key=value / key: value pairs whose key looks like a credential (e.g. "sasl.password=...").
    [GeneratedRegex(@"(?i)\b((?:sasl\.|ssl\.)?(?:password|passwd|secret|api[_.-]?secret|key\.password|keystore\.password|basic\.auth\.user\.info|sasl\.jaas\.config)\s*[=:]\s*)(""[^""]*""|'[^']*'|[^\s,;]+)")]
    private static partial Regex CredentialAssignment();

    /// <summary>Replaces every occurrence of the given secret values and any credential-looking assignment.</summary>
    public static string Redact(string? text, IEnumerable<string?>? secrets = null)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;

        var result = text;
        if (secrets != null)
        {
            foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)).Distinct().OrderByDescending(s => s!.Length))
                result = result.Replace(secret!, Mask, StringComparison.Ordinal);
        }

        return CredentialAssignment().Replace(result, m => m.Groups[1].Value + Mask);
    }
}
