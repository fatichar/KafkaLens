using System;
using System.Diagnostics;

namespace AvaloniaApp.Utils;

public static class OsUtils
{
    public static bool OpenExternal(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
            return false;

        try
        {
            // Use platform-specific safe launching
            if (OperatingSystem.IsWindows())
            {
                Process.Start(new ProcessStartInfo("cmd", $"/c start \"\" \"{target}\"") { CreateNoWindow = true });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", target);
            }
            else if (OperatingSystem.IsLinux())
            {
                Process.Start("xdg-open", target);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}