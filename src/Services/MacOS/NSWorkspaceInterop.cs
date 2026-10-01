#if !WINDOWS
namespace DoriDeck.Services.MacOS;

internal static class NSWorkspaceInterop
{
	public static int GetFrontmostApplicationProcessId()
	{
		using var pool = new NSAutoreleasePoolScope();

		var workspaceClass = ObjectiveCRuntime.GetClass("NSWorkspace");
		var sharedWorkspace = ObjectiveCRuntime.Send(workspaceClass, ObjectiveCRuntime.Selector("sharedWorkspace"));
		var frontmostApplication = ObjectiveCRuntime.Send(sharedWorkspace, ObjectiveCRuntime.Selector("frontmostApplication"));

		if (frontmostApplication == IntPtr.Zero)
		{
			return 0;
		}

		return ObjectiveCRuntime.SendInt32(frontmostApplication, ObjectiveCRuntime.Selector("processIdentifier"));
	}
}
#endif
