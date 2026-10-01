#if !WINDOWS
using System.Runtime.InteropServices;

namespace DoriDeck.Services.MacOS;

/// <summary>
/// NSPasteboard access for the general (system) pasteboard. Reads go through CoreFoundation
/// (NSArray/NSString/NSData are toll-free bridged with CFArray/CFString/CFData, so no reference
/// counting is needed on the way out). Writes go through Objective-C messages, since building a new
/// NSPasteboardItem/NSArray has no plain-C equivalent.
/// </summary>
internal static class NSPasteboardInterop
{
	private const string PlainTextUti = "public.utf8-plain-text";

	public static uint GetChangeCount()
	{
		using var pool = new NSAutoreleasePoolScope();
		return unchecked((uint)ObjectiveCRuntime.SendInt64(GeneralPasteboard(), Selector("changeCount")));
	}

	public static string? ReadUnicodeText()
	{
		using var pool = new NSAutoreleasePoolScope();

		var pasteboard = GeneralPasteboard();
		var typeString = CoreFoundationInterop.CreateString(PlainTextUti);
		try
		{
			var value = ObjectiveCRuntime.Send(pasteboard, Selector("stringForType:"), typeString);
			return CoreFoundationInterop.StringToManaged(value);
		}
		finally
		{
			CoreFoundationInterop.Release(typeString);
		}
	}

	public static void WriteUnicodeText(string text)
	{
		using var pool = new NSAutoreleasePoolScope();

		var pasteboard = GeneralPasteboard();
		ObjectiveCRuntime.Send(pasteboard, Selector("clearContents"));

		var typeString = CoreFoundationInterop.CreateString(PlainTextUti);
		var valueString = CoreFoundationInterop.CreateString(text);
		try
		{
			ObjectiveCRuntime.Send(pasteboard, Selector("setString:forType:"), valueString, typeString);
		}
		finally
		{
			CoreFoundationInterop.Release(typeString);
			CoreFoundationInterop.Release(valueString);
		}
	}

	/// <summary>Captures every item currently on the pasteboard, each as its (UTI type, raw bytes) pairs.</summary>
	public static List<List<(string Type, byte[] Data)>> CaptureItems()
	{
		using var pool = new NSAutoreleasePoolScope();

		var pasteboard = GeneralPasteboard();
		var itemsArray = ObjectiveCRuntime.Send(pasteboard, Selector("pasteboardItems"));
		var itemCount = CoreFoundationInterop.ArrayCount(itemsArray);

		var items = new List<List<(string, byte[])>>();

		for (var i = 0; i < itemCount; i++)
		{
			var item = CoreFoundationInterop.ArrayValueAt(itemsArray, i);
			var typesArray = ObjectiveCRuntime.Send(item, Selector("types"));
			var typeCount = CoreFoundationInterop.ArrayCount(typesArray);

			var formats = new List<(string, byte[])>();

			for (var t = 0; t < typeCount; t++)
			{
				var typeStringRef = CoreFoundationInterop.ArrayValueAt(typesArray, t);
				var typeName = CoreFoundationInterop.StringToManaged(typeStringRef);
				if (typeName is null)
				{
					continue;
				}

				var data = ObjectiveCRuntime.Send(item, Selector("dataForType:"), typeStringRef);
				var bytes = CoreFoundationInterop.DataToManaged(data);
				if (bytes is not null)
				{
					formats.Add((typeName, bytes));
				}
			}

			items.Add(formats);
		}

		return items;
	}

	/// <summary>Replaces the pasteboard's contents with a snapshot previously captured by <see cref="CaptureItems"/>.</summary>
	public static void RestoreItems(IReadOnlyList<List<(string Type, byte[] Data)>> items)
	{
		using var pool = new NSAutoreleasePoolScope();

		var pasteboard = GeneralPasteboard();
		ObjectiveCRuntime.Send(pasteboard, Selector("clearContents"));

		if (items.Count == 0)
		{
			return;
		}

		var itemClass = ObjectiveCRuntime.GetClass("NSPasteboardItem");
		var createdItems = new IntPtr[items.Count];

		for (var i = 0; i < items.Count; i++)
		{
			var item = ObjectiveCRuntime.Send(ObjectiveCRuntime.Send(itemClass, Selector("alloc")), Selector("init"));

			foreach (var (type, data) in items[i])
			{
				var typeString = CoreFoundationInterop.CreateString(type);
				var cfData = CoreFoundationInterop.CreateData(data);
				try
				{
					ObjectiveCRuntime.Send(item, Selector("setData:forType:"), cfData, typeString);
				}
				finally
				{
					CoreFoundationInterop.Release(typeString);
					CoreFoundationInterop.Release(cfData);
				}
			}

			createdItems[i] = item;
		}

		var pinnedItems = GCHandle.Alloc(createdItems, GCHandleType.Pinned);
		try
		{
			var arrayClass = ObjectiveCRuntime.GetClass("NSArray");
			var array = ObjectiveCRuntime.Send(
				ObjectiveCRuntime.Send(arrayClass, Selector("alloc")),
				Selector("initWithObjects:count:"),
				pinnedItems.AddrOfPinnedObject(),
				(IntPtr)createdItems.Length);

			ObjectiveCRuntime.Send(pasteboard, Selector("writeObjects:"), array);
			ObjectiveCRuntime.Send(array, Selector("release"));
		}
		finally
		{
			pinnedItems.Free();
		}

		foreach (var item in createdItems)
		{
			ObjectiveCRuntime.Send(item, Selector("release"));
		}
	}

	private static IntPtr GeneralPasteboard() =>
		ObjectiveCRuntime.Send(ObjectiveCRuntime.GetClass("NSPasteboard"), Selector("generalPasteboard"));

	private static IntPtr Selector(string name) => ObjectiveCRuntime.Selector(name);
}
#endif
