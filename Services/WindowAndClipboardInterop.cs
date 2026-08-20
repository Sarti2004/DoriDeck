using System.Runtime.InteropServices;

namespace DoriDeck.Services;

internal static class WindowAndClipboardInterop
{
    public static IntPtr GetForegroundWindowHandle() =>
        GetForegroundWindow();

    public static uint GetForegroundWindowProcessId(IntPtr window) =>
        GetWindowThreadProcessId(window, out var processId) == 0
            ? 0
            : processId;

    public static uint GetClipboardSequence() =>
        GetClipboardSequenceNumber();

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(
        IntPtr window,
        out uint processId);

    [DllImport("user32.dll")]
    private static extern uint GetClipboardSequenceNumber();
}
