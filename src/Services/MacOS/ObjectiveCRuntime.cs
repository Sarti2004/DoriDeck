#if !WINDOWS
using System.Runtime.InteropServices;

namespace DoriDeck.Services.MacOS;

/// <summary>
/// Minimal Objective-C runtime bindings used to call the small number of AppKit/Foundation
/// selectors (NSWorkspace, NSPasteboard, NSPasteboardItem, NSArray) that have no plain-C
/// equivalent. Everything else goes through CoreFoundation, which is toll-free bridged with
/// NSString/NSArray/NSData and does not need objc_msgSend.
/// </summary>
internal static class ObjectiveCRuntime
{
	private const string ObjCLibrary = "/usr/lib/libobjc.dylib";
	private const string AppKitFramework = "/System/Library/Frameworks/AppKit.framework/AppKit";

	static ObjectiveCRuntime()
	{
		// it looks like it doesn't work without it.
		NativeLibrary.Load(AppKitFramework);
	}

	[DllImport(ObjCLibrary, CharSet = CharSet.Ansi, BestFitMapping = false)]
	private static extern IntPtr objc_getClass(string name);

	[DllImport(ObjCLibrary, CharSet = CharSet.Ansi, BestFitMapping = false)]
	private static extern IntPtr sel_registerName(string name);

	[DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

	[DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

	[DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
	private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2);

	[DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
	private static extern int objc_msgSend_int(IntPtr receiver, IntPtr selector);

	[DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
	private static extern long objc_msgSend_long(IntPtr receiver, IntPtr selector);

	[DllImport(ObjCLibrary, EntryPoint = "objc_msgSend")]
	private static extern byte objc_msgSend_bool_ulong(IntPtr receiver, IntPtr selector, ulong arg1);

	public static IntPtr GetClass(string name) => objc_getClass(name);

	public static IntPtr Selector(string name) => sel_registerName(name);

	public static IntPtr Send(IntPtr receiver, IntPtr selector) =>
		receiver == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(receiver, selector);

	public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1) =>
		receiver == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(receiver, selector, arg1);

	public static IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2) =>
		receiver == IntPtr.Zero ? IntPtr.Zero : objc_msgSend(receiver, selector, arg1, arg2);

	public static int SendInt32(IntPtr receiver, IntPtr selector) =>
		receiver == IntPtr.Zero ? 0 : objc_msgSend_int(receiver, selector);

	public static long SendInt64(IntPtr receiver, IntPtr selector) =>
		receiver == IntPtr.Zero ? 0 : objc_msgSend_long(receiver, selector);

	public static bool SendBool(IntPtr receiver, IntPtr selector, ulong arg1) =>
		receiver != IntPtr.Zero && objc_msgSend_bool_ulong(receiver, selector, arg1) != 0;
}

/// <summary>
/// Wraps an NSAutoreleasePool for the lifetime of a manual objc_msgSend call sequence. Needed
/// because this process never runs an AppKit event loop of its own, so nothing else drains the
/// autorelease pool for convenience constructors like <c>+[NSArray arrayWithObjects:count:]</c>.
/// </summary>
internal readonly struct NSAutoreleasePoolScope : IDisposable
{
	private readonly IntPtr _pool;

	public NSAutoreleasePoolScope()
	{
		var poolClass = ObjectiveCRuntime.GetClass("NSAutoreleasePool");
		var alloc = ObjectiveCRuntime.Send(poolClass, ObjectiveCRuntime.Selector("alloc"));
		_pool = ObjectiveCRuntime.Send(alloc, ObjectiveCRuntime.Selector("init"));
	}

	public void Dispose()
	{
		if (_pool != IntPtr.Zero)
		{
			ObjectiveCRuntime.Send(_pool, ObjectiveCRuntime.Selector("drain"));
		}
	}
}
#endif
