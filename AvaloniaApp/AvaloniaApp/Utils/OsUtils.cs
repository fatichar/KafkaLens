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
                using var proc = Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            else if (OperatingSystem.IsMacOS())
            {
                using var proc = Process.Start(new ProcessStartInfo("open")
                {
                    ArgumentList = { target }
                });
            }
            else if (OperatingSystem.IsLinux())
            {
                using var proc = Process.Start(new ProcessStartInfo("xdg-open")
                {
                    ArgumentList = { target }
                });
            }

            return true;
        }
        catch
        {
            return false;
        }
    }
}