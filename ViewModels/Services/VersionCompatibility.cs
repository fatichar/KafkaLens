using System.Text.RegularExpressions;

namespace KafkaLens.ViewModels.Services;

public static class VersionCompatibility
{
    private static readonly Regex ConstraintRegex =
        new(@"^(>=|>|<=|<|=)\s*(\d+\.\d+\.\d+(?:\.\d+)?)$", RegexOptions.Compiled);

    /// <summary>
    /// Returns true if <paramref name="appVersion"/> satisfies the constraint expression.
    /// An empty or unrecognised constraint is treated as always compatible.
    /// </summary>
    public static bool IsCompatible(string constraint, string appVersion)
    {
        if (string.IsNullOrWhiteSpace(constraint))
            return true;

        var match = ConstraintRegex.Match(constraint.Trim());
        if (!match.Success)
            return true;

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
