
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DoriDeck.Services.Windows;

internal static class WindowsApplicationInterop
{
    public static bool ActivateDorico()
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                if (!process.ProcessName.StartsWith("Dorico",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                var hwnd = process.MainWindowHandle;
                if (hwnd == IntPtr.Zero)
                    continue;

                // Already foreground
                if (GetForegroundWindow() == hwnd)
                    return true;

                if (IsIconic(hwnd))
                    ShowWindowAsync(hwnd, 9); // SW_RESTORE

                SetForegroundWindow(hwnd);

                if (GetForegroundWindow() == hwnd)
                    return true;
            }
        }

        return false;
    }


    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr hwnd, int command);

}
