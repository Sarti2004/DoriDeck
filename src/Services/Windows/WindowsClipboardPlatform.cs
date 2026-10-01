using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using DoriDeck.Services;

namespace DoriDeck.Services.Windows;

internal sealed class WindowsClipboardPlatform : IClipboardPlatform
{
    private const uint UnicodeText = 13; // CF_UNICODETEXT

    public string? ReadUnicodeText(int retryCount, TimeSpan retryDelay)
    {
        OpenWithRetry(IntPtr.Zero, retryCount, retryDelay);

        try
        {
            if (!IsClipboardFormatAvailable(UnicodeText))
            {
                return null;
            }

            var handle = GetClipboardData(UnicodeText);
            if (handle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            var pointer = GlobalLock(handle);
            if (pointer == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                return Marshal.PtrToStringUni(pointer);
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    public void WriteUnicodeText(string text, int retryCount, TimeSpan retryDelay)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = Encoding.Unicode.GetBytes(text + '\0');
        var memory = GlobalAlloc(0x0002, (nuint)bytes.Length); // GMEM_MOVEABLE
        if (memory == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error());
        }

        try
        {
            var pointer = GlobalLock(memory);
            if (pointer == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                Marshal.Copy(bytes, 0, pointer, bytes.Length);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            // EmptyClipboard requires an owner window before SetClipboardData.
            var window = CreateWindowExW(0, "STATIC", "", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (window == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            try
            {
                OpenWithRetry(window, retryCount, retryDelay);
                try
                {
                    if (!EmptyClipboard() || SetClipboardData(UnicodeText, memory) == IntPtr.Zero)
                    {
                        throw new Win32Exception(Marshal.GetLastWin32Error());
                    }

                    memory = IntPtr.Zero; // The clipboard now owns the allocation.
                }
                finally
                {
                    CloseClipboard();
                }
            }
            finally
            {
                DestroyWindow(window);
            }
        }
        finally
        {
            if (memory != IntPtr.Zero)
            {
                GlobalFree(memory);
            }
        }
    }

    private static void OpenWithRetry(IntPtr window, int retryCount, TimeSpan retryDelay)
    {
        for (var attempt = 0; !OpenClipboard(window); attempt++)
        {
            if (attempt >= retryCount - 1)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            Thread.Sleep(retryDelay);
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetClipboardData(uint format, IntPtr handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className,
        string windowName, uint style, int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalAlloc(uint flags, nuint bytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr handle);
}
