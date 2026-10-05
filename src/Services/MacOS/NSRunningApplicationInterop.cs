#if !WINDOWS
namespace DoriDeck.Services.MacOS;

// can't do working check if Dorico is running, so just focus it.
internal static class NSRunningApplicationInterop
{

	private const string DoricoBundleIdentifierPrefix = "com.steinberg.dorico";

	public static bool ActivateDorico()
	{
		using var pool = new NSAutoreleasePoolScope();

		var workspaceClass = ObjectiveCRuntime.GetClass("NSWorkspace");
		var sharedWorkspace = ObjectiveCRuntime.Send(workspaceClass, ObjectiveCRuntime.Selector("sharedWorkspace"));
		var runningApplications = ObjectiveCRuntime.Send(sharedWorkspace, ObjectiveCRuntime.Selector("runningApplications"));

		var count = CoreFoundationInterop.ArrayCount(runningApplications);
		for (var i = 0; i < count; i++)
		{
			var application = CoreFoundationInterop.ArrayValueAt(runningApplications, i);
			var bundleIdentifier = CoreFoundationInterop.StringToManaged(
				ObjectiveCRuntime.Send(application, ObjectiveCRuntime.Selector("bundleIdentifier")));

			if (bundleIdentifier is not null &&
				bundleIdentifier.StartsWith(DoricoBundleIdentifierPrefix, StringComparison.OrdinalIgnoreCase))
			{
				return ObjectiveCRuntime.SendBool(application, ObjectiveCRuntime.Selector("activateWithOptions:"), 0);
			}
		}

		return false;
	}
}
#endif
