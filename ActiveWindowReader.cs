
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

public static class ActiveWindowReader
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetWindowText(
        IntPtr hWnd,
        StringBuilder text,
        int count);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr hWnd,
        out uint processId);

    public static string? GetActiveDoricoWindowTitle()
    {
        IntPtr windowHandle = GetForegroundWindow();

        if (windowHandle == IntPtr.Zero)
            return null;

        GetWindowThreadProcessId(windowHandle, out uint processId);

        using Process process = Process.GetProcessById((int)processId);

        if (!process.ProcessName.Contains(
                "Dorico",
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var title = new StringBuilder(512);

        int length = GetWindowText(
            windowHandle,
            title,
            title.Capacity);

        return length > 0
            ? title.ToString()
            : null;
    }
}
