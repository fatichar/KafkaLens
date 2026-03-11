using System.Text.RegularExpressions;
using Serilog;

namespace KafkaLens.ViewModels.Services;

public static class VersionCompatibility
{
    // Valid format: operator followed by a 3- or 4-component version, e.g. ">=1.2.3" or "=1.0.0.0"
    private static readonly Regex ConstraintRegex =
        new(@"^(>=|>|<=|<|=)\s*(\d+\.\d+\.\d+(?:\.\d+)?)$", RegexOptions.Compiled);

    /// <summary>
    /// Returns <c>true</c> if <paramref name="appVersion"/> satisfies
    /// <paramref name="constraint"/>.
    /// <para>
    /// Supported operators: <c>&gt;=</c>, <c>&gt;</c>, <c>&lt;=</c>, <c>&lt;</c>, <c>=</c>
    /// followed by a 3- or 4-part version number (e.g. <c>&gt;=1.2.0</c>).
    /// </para>
    /// An empty constraint is treated as always compatible.
    /// A non-empty but unrecognised constraint is also treated as compatible and a
    /// warning is logged so repository authors can spot typos.
    /// </summary>
    public static bool IsCompatible(string constraint, string appVersion)
    {
        if (string.IsNullOrWhiteSpace(constraint))
            return true;

        var match = ConstraintRegex.Match(constraint.Trim());
        if (!match.Success)
        {
            Log.Warning(
                "Unrecognised version constraint '{Constraint}' — expected format: " +
                "operator followed by a 3-part version (e.g. '>=1.2.0'). " +
                "Treating as compatible.",
                constraint);
            return true;
        }

        if (!Version.TryParse(match.Groups[2].Value, out var required))
            return true;

        if (!Version.TryParse(appVersion, out var current))
            return true;

        return match.Groups[1].Value switch
        {
            ">=" => current >= required,
            ">"  => current >  required,
            "<=" => current <= required,
            "<"  => current <  required,
            "="  => current == required,
            _    => true
        };
    }
}
