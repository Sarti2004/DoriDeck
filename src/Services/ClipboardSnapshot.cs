using DoriDeck.Services.MacOS;
using DoriDeck.Services.Windows;

namespace DoriDeck.Services;

/// Reads and writes clipboard text through the platform implementation.
internal static class ClipboardSnapshot
{
	private static readonly IClipboardPlatform Platform = CreatePlatform();

	public static string? ReadUnicodeText(int retryCount, TimeSpan retryDelay) =>
		Platform.ReadUnicodeText(retryCount, retryDelay);

	public static void WriteUnicodeText(string text, int retryCount, TimeSpan retryDelay) =>
		Platform.WriteUnicodeText(text, retryCount, retryDelay);

	private static IClipboardPlatform CreatePlatform()
	{
		if (OperatingSystem.IsWindows())
		{
			return new WindowsClipboardPlatform();
		}

		if (OperatingSystem.IsMacOS())
		{
			return new MacOSClipboardPlatform();
		}

		throw new PlatformNotSupportedException(
			"Clipboard snapshots are supported only on Windows and macOS.");
	}
}
