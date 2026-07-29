using System.Diagnostics;

namespace DoriDeck.Services;

public sealed class ApplicationFocusService : IApplicationFocusService
{
    public bool IsForeground(string processNamePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processNamePrefix);

        var window =
            WindowAndClipboardInterop.GetForegroundWindowHandle();

        if (window == IntPtr.Zero)
        {
            return false;
        }

        var processId =
            WindowAndClipboardInterop.GetForegroundWindowProcessId(window);

        try
        {
            using var process = Process.GetProcessById(
                checked((int)processId));

            return process.ProcessName.StartsWith(
                processNamePrefix,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            // The process may have exited between obtaining its ID
            // and opening the Process object.
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    public void EnsureForeground(
        string processNamePrefix,
        string? errorMessage = null)
    {
        if (!IsForeground(processNamePrefix))
        {
            throw new InvalidOperationException(
                errorMessage ??
                $"{processNamePrefix} must be the foreground application.");
        }
    }
}