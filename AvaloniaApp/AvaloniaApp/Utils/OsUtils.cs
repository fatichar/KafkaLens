using System.Diagnostics;

namespace AvaloniaApp.Utils;

public static class OsUtils
{
    public static bool OpenExternal(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            };
            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
