using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace DoriDeck.Services.Windows;

// Windows UI automation to read and select dropdown items
[SupportedOSPlatform("windows")]
internal static class WindowsUiAutomation
{
	private const int TreeScopeDescendants = 4;
	private const int ControlTypePropertyId = 30003;
	private const int ListItemControlTypeId = 50007;
	private const int SelectionItemPatternId = 10010;

	private static readonly Guid CUIAutomationClsid = new("ff48dba4-60ef-4201-aa87-54103eef594e");

	/// Selects the item from dropdown. copy/paste from stackoverflow.	
	public static bool SelectDropdownItem(string itemName)
	{
		var automation = (IUIAutomation)Activator.CreateInstance(Type.GetTypeFromCLSID(CUIAutomationClsid, throwOnError: true)!)!;

		var focused = automation.GetFocusedElement();
		if (focused == null)
		{
			return false;
		}

		var condition = automation.CreatePropertyCondition(ControlTypePropertyId, ListItemControlTypeId);

		if (TrySelect(focused, condition, itemName))
		{
			return true;
		}

		if (focused.GetCurrentControlType() != ListItemControlTypeId)
		{
			return false;
		}

		var parent = automation.GetControlViewWalker().GetParentElement(focused);
		return parent != null && TrySelect(parent, condition, itemName);
	}

	private static bool TrySelect(IUIAutomationElement root, IUIAutomationCondition condition, string itemName)
	{
		var items = root.FindAll(TreeScopeDescendants, condition);
		if (items == null)
		{
			return false;
		}

		var count = items.GetLength();
		for (var i = 0; i < count; i++)
		{
			var item = items.GetElement(i);
			if (!string.Equals(item.GetCurrentName(), itemName, StringComparison.Ordinal))
			{
				continue;
			}

			if (item.GetCurrentPattern(SelectionItemPatternId) is IUIAutomationSelectionItemPattern pattern)
			{
				pattern.Select();
				return true;
			}
		}

		return false;
	}

	[ComImport, Guid("30cbe57d-d9d0-452a-ab13-7ac5ac4825ee"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IUIAutomation
	{
		void CompareElements();
		void CompareRuntimeIds();
		void GetRootElement();
		void ElementFromHandle();
		void ElementFromPoint();
		IUIAutomationElement? GetFocusedElement();
		void GetRootElementBuildCache();
		void ElementFromHandleBuildCache();
		void ElementFromPointBuildCache();
		void GetFocusedElementBuildCache();
		void CreateTreeWalker();
		IUIAutomationTreeWalker GetControlViewWalker();
		void GetContentViewWalker();
		void GetRawViewWalker();
		void GetRawViewCondition();
		void GetControlViewCondition();
		void GetContentViewCondition();
		void CreateCacheRequest();
		void CreateTrueCondition();
		void CreateFalseCondition();
		IUIAutomationCondition CreatePropertyCondition(int propertyId, [MarshalAs(UnmanagedType.Struct)] object value);
	}

	[ComImport, Guid("d22108aa-8ac5-49a5-837b-37bbb3d7591e"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IUIAutomationElement
	{
		void SetFocus();
		void GetRuntimeId();
		void FindFirst();
		IUIAutomationElementArray? FindAll(int scope, IUIAutomationCondition condition);
		void FindFirstBuildCache();
		void FindAllBuildCache();
		void BuildUpdatedCache();
		void GetCurrentPropertyValue();
		void GetCurrentPropertyValueEx();
		void GetCachedPropertyValue();
		void GetCachedPropertyValueEx();
		void GetCurrentPatternAs();
		void GetCachedPatternAs();
		[return: MarshalAs(UnmanagedType.IUnknown)]
		object? GetCurrentPattern(int patternId);
		void GetCachedPattern();
		void GetCachedParent();
		void GetCachedChildren();
		int GetCurrentProcessId();
		int GetCurrentControlType();
		[return: MarshalAs(UnmanagedType.BStr)]
		string GetCurrentLocalizedControlType();
		[return: MarshalAs(UnmanagedType.BStr)]
		string GetCurrentName();
	}

	[ComImport, Guid("14314595-b4bc-4055-95f2-58f2e42c9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IUIAutomationElementArray
	{
		int GetLength();
		IUIAutomationElement GetElement(int index);
	}

	[ComImport, Guid("4042c624-389c-4afc-a630-9df854a541fc"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IUIAutomationTreeWalker
	{
		IUIAutomationElement? GetParentElement(IUIAutomationElement element);
	}

	[ComImport, Guid("352ffba8-0973-437c-a61f-f64cafd81df9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IUIAutomationCondition
	{
	}

	[ComImport, Guid("a8efa66a-0fda-421a-9194-38021f3578ea"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IUIAutomationSelectionItemPattern
	{
		void Select();
	}
}
