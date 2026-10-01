#if !WINDOWS
using System.Runtime.InteropServices;

namespace DoriDeck.Services.MacOS;

/// <summary>
/// CoreFoundation bindings for the handful of operations used to read and build NSString, NSArray
/// and NSData values. CFString/NSString, CFArray/NSArray and CFData/NSData are toll-free bridged,
/// so a pointer obtained from an Objective-C message can be passed directly into these functions.
/// </summary>
internal static class CoreFoundationInterop
{
	private const string CoreFoundationLibrary =
		"/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

	[StructLayout(LayoutKind.Sequential)]
	private struct CFRange
	{
		public long Location;
		public long Length;
	}

	[DllImport(CoreFoundationLibrary)]
	private static extern void CFRelease(IntPtr cf);

	[DllImport(CoreFoundationLibrary)]
	private static extern IntPtr CFStringCreateWithCharacters(IntPtr alloc, char[] chars, long numChars);

	[DllImport(CoreFoundationLibrary)]
	private static extern long CFStringGetLength(IntPtr theString);

	[DllImport(CoreFoundationLibrary)]
	private static extern void CFStringGetCharacters(IntPtr theString, CFRange range, char[] buffer);

	[DllImport(CoreFoundationLibrary)]
	private static extern long CFArrayGetCount(IntPtr theArray);

	[DllImport(CoreFoundationLibrary)]
	private static extern IntPtr CFArrayGetValueAtIndex(IntPtr theArray, long idx);

	[DllImport(CoreFoundationLibrary)]
	private static extern long CFDataGetLength(IntPtr theData);

	[DllImport(CoreFoundationLibrary)]
	private static extern IntPtr CFDataGetBytePtr(IntPtr theData);

	[DllImport(CoreFoundationLibrary)]
	private static extern IntPtr CFDataCreate(IntPtr allocator, byte[] bytes, long length);

	public static void Release(IntPtr cf)
	{
		if (cf != IntPtr.Zero)
		{
			CFRelease(cf);
		}
	}

	public static IntPtr CreateString(string value)
	{
		var chars = value.ToCharArray();
		return CFStringCreateWithCharacters(IntPtr.Zero, chars, chars.Length);
	}

	public static string? StringToManaged(IntPtr cfString)
	{
		if (cfString == IntPtr.Zero)
		{
			return null;
		}

		var length = CFStringGetLength(cfString);
		if (length <= 0)
		{
			return string.Empty;
		}

		var buffer = new char[length];
		CFStringGetCharacters(cfString, new CFRange { Location = 0, Length = length }, buffer);
		return new string(buffer);
	}

	public static long ArrayCount(IntPtr array) =>
		array == IntPtr.Zero ? 0 : CFArrayGetCount(array);

	public static IntPtr ArrayValueAt(IntPtr array, long index) =>
		CFArrayGetValueAtIndex(array, index);

	public static byte[]? DataToManaged(IntPtr data)
	{
		if (data == IntPtr.Zero)
		{
			return null;
		}

		var length = CFDataGetLength(data);
		if (length <= 0)
		{
			return [];
		}

		var bytes = CFDataGetBytePtr(data);
		if (bytes == IntPtr.Zero)
		{
			return null;
		}

		var buffer = new byte[length];
		Marshal.Copy(bytes, buffer, 0, (int)length);
		return buffer;
	}

	public static IntPtr CreateData(byte[] bytes) =>
		CFDataCreate(IntPtr.Zero, bytes, bytes.Length);
}
#endif
