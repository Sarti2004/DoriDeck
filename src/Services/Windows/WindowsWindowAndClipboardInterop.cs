using System.Runtime.InteropServices;
using DoriDeck.Services;

namespace DoriDeck.Services.Windows;

internal sealed class WindowsWindowAndClipboardInterop : IWindowAndClipboardInterop
{
	public IntPtr GetForegroundWindowHandle() =>
		GetForegroundWindow();

	public uint GetForegroundWindowProcessId(IntPtr window) =>
		GetWindowThreadProcessId(window, out var processId) == 0
			? 0
			: processId;

	public string? GetForegroundWindowTitle(IntPtr window)
	{
		var title = new char[512];
		var length = GetWindowTextW(window, title, title.Length);

		return length > 0 ? new string(title, 0, length) : null;
	}

	public uint GetClipboardSequence() =>
		GetClipboardSequenceNumber();

	[DllImport("user32.dll")]
	private static extern IntPtr GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(
		IntPtr window,
		out uint processId);

	[DllImport("user32.dll", EntryPoint = "GetWindowTextW", ExactSpelling = true, CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] text, int count);

	[DllImport("user32.dll")]
	private static extern uint GetClipboardSequenceNumber();
}
