namespace DoriDeck.Services;

internal interface IWindowAndClipboardInterop
{
	IntPtr GetForegroundWindowHandle();
	uint GetForegroundWindowProcessId(IntPtr window);
	string? GetForegroundWindowTitle(IntPtr window);
	uint GetClipboardSequence();
}
