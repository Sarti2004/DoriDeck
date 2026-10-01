using System.Diagnostics;
using DoriDeck.Services;

namespace DoriDeck.Services;

public static class ActiveWindowReader
{
	public static string? GetActiveDoricoWindowTitle()
	{
		var windowHandle = WindowAndClipboardInterop.GetForegroundWindowHandle();
		if (windowHandle == IntPtr.Zero)
		{
			return null;
		}

		var processId = WindowAndClipboardInterop.GetForegroundWindowProcessId(windowHandle);
		using var process = Process.GetProcessById((int)processId);

		if (!process.ProcessName.Contains("Dorico", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		return WindowAndClipboardInterop.GetForegroundWindowTitle(windowHandle);
	}
}
