using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DoriDeck.Services;

public static class ActiveWindowReader
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(
        IntPtr hWnd,
        StringBuilder text,
        int count);

    public static string? GetActiveDoricoWindowTitle()
    {
        IntPtr windowHandle = WindowAndClipboardInterop.GetForegroundWindowHandle();

        if (windowHandle == IntPtr.Zero)
            return null;

        var processId = WindowAndClipboardInterop.GetForegroundWindowProcessId(windowHandle);

        using Process process = Process.GetProcessById((int)processId);

        if (!process.ProcessName.Contains("Dorico", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var title = new StringBuilder(512);
        int length = GetWindowText(windowHandle, title, title.Capacity);

        return length > 0 ? title.ToString() : null;
    }
}
