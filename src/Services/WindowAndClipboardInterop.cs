using DoriDeck.Services.MacOS;
using DoriDeck.Services.Windows;

namespace DoriDeck.Services;

/// <summary>
/// Cross-platform facade for foreground-window and clipboard sequence information.
/// </summary>
internal static class WindowAndClipboardInterop
{
	private static readonly IWindowAndClipboardInterop Implementation = CreateImplementation();

	public static IntPtr GetForegroundWindowHandle() =>
		Implementation.GetForegroundWindowHandle();

	public static uint GetForegroundWindowProcessId(IntPtr window) =>
		Implementation.GetForegroundWindowProcessId(window);

	public static string? GetForegroundWindowTitle(IntPtr window) =>
		Implementation.GetForegroundWindowTitle(window);

	public static uint GetClipboardSequence() =>
		Implementation.GetClipboardSequence();

	private static IWindowAndClipboardInterop CreateImplementation()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsWindowAndClipboardInterop();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOSWindowAndClipboardInterop();
		}

		throw new PlatformNotSupportedException(
			"Window and clipboard interop is supported only on Windows and macOS.");
	}
}
