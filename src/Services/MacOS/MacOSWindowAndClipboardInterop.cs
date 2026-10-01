using DoriDeck.Services;

namespace DoriDeck.Services.MacOS;

/// <summary>
/// macOS has no cross-process window handle equivalent to a Win32 HWND. The value exposed as the
/// "window handle" is therefore the frontmost application's process id. It is passed through by
/// <see cref="GetForegroundWindowProcessId"/> and used by <see cref="GetForegroundWindowTitle"/>
/// to query the focused window through the Accessibility API.
/// </summary>
internal sealed class MacOSWindowAndClipboardInterop : IWindowAndClipboardInterop
{
	public IntPtr GetForegroundWindowHandle() =>
		(IntPtr)NSWorkspaceInterop.GetFrontmostApplicationProcessId();

	public uint GetForegroundWindowProcessId(IntPtr window) =>
		window == IntPtr.Zero ? 0 : unchecked((uint)(long)window);

	public string? GetForegroundWindowTitle(IntPtr window) =>
		window == IntPtr.Zero
			? null
			: AccessibilityInterop.GetFocusedWindowTitle(unchecked((int)(long)window));

	public uint GetClipboardSequence() =>
		NSPasteboardInterop.GetChangeCount();
}
