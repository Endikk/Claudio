using System.ComponentModel;
using System.Diagnostics;

namespace Claudio.App;

/// <summary>Opening things in the user's own programs, which may not be there.</summary>
internal static class Shell
{
    /// <summary>
    /// Opens a web page in the default browser. No default browser, a policy that forbids it, a
    /// handler that is gone: all of it comes back as false and a line in the log, never as an
    /// exception out of a click.
    /// </summary>
    public static bool Open(Uri address)
    {
        try
        {
            Process.Start(new ProcessStartInfo(address.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            DiagnosticLog.Append($"could not open {address.Host}: {error.Message}");
            return false;
        }
    }
}
