using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace KafkaLens.ViewModels.Telemetry;

/// <summary>
/// Builds a privacy-safe description of an exception: types and method names only.
/// Exception messages, file paths, line numbers and arguments are never included.
/// </summary>
public static class ErrorReport
{
    private const int MaxFrames = 6;
    private const int MaxFramesLength = 480;

    public static Dictionary<string, object?> Build(Exception exception, string kind)
    {
        var root = Unwrap(exception);
        var frames = GetFrames(root);
        var framesText = string.Join(" < ", frames.Take(MaxFrames));
        if (framesText.Length > MaxFramesLength) framesText = framesText[..MaxFramesLength];

        return new Dictionary<string, object?>
        {
            ["kind"] = kind,
            ["exception_type"] = root.GetType().FullName,
            ["inner_type"] = root.InnerException?.GetType().FullName,
            ["frames"] = framesText,
            ["fingerprint"] = Fingerprint(root.GetType().FullName ?? "", frames),
        };
    }

    private static Exception Unwrap(Exception exception)
    {
        var current = exception;
        while (current is AggregateException { InnerExceptions.Count: 1 } agg)
            current = agg.InnerExceptions[0];
        while (current is System.Reflection.TargetInvocationException { InnerException: { } inner })
            current = inner;
        return current;
    }

    private static List<string> GetFrames(Exception exception)
    {
        var result = new List<string>();
        try
        {
            foreach (var frame in new StackTrace(exception, false).GetFrames())
            {
                var method = frame.GetMethod();
                if (method == null) continue;
                var type = method.DeclaringType?.FullName ?? "?";
                // Compiler-generated async/lambda types: keep the outer type name readable.
                var name = $"{type}.{method.Name}";
                if (result.Count == 0 || result[^1] != name) result.Add(name);
            }
        }
        catch (Exception)
        {
            // Stack trace unavailable — report type only.
        }
        return result;
    }

    private static string Fingerprint(string type, IEnumerable<string> frames)
    {
        var input = type + "|" + string.Join("|", frames);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash, 0, 6).ToLowerInvariant();
    }
}
