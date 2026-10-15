using System.Diagnostics;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface.Commands;

namespace DoriDeck.Actions;

/// <summary>
/// Opens the first project in Dorico's recent file list.
/// </summary>
internal sealed class OpenRecentFileAction(DoricoSession session) : DoriDeckActionBase(session), IActionDefinition
{
	public string Id => "open-recent-file";

	public LocalizedText Name => "Open Recent File";

	public LocalizedText Description => "Opens the first project in Dorico's recent file list.";

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOs;

	public IActionExecutor CreateExecutor() => new Executor(Session);

	/// Opens the entry at <paramref name="index"/> of Dorico's recent file list.
	internal static async Task<ActionResult> OpenRecentFileAsync(
		DoricoSession session,
		int index,
		CancellationToken cancellationToken)
	{
		var recentFiles = session.RecentFiles;
		if (recentFiles.Count <= index)
		{
			return ActionResult.Failed(ActionErrorCodes.Unavailable, "no files found");
		}

		var dorico = await GetConnectedRemoteAsync(session, cancellationToken);
		if (dorico == null)
		{
			// Dorico isn't running: let the OS open the project.
			return OpenWithShell(recentFiles[index]);
		}

		await dorico.SendRequestAsync(
			new Command("File.Open", new CommandParameter("File", recentFiles[index])),
			cancellationToken);

		return ActionResult.Success();
	}

	private static ActionResult OpenWithShell(string filePath)
	{
		var fullPath = Path.GetFullPath(filePath);
		if (!File.Exists(fullPath))
		{
			return ActionResult.Failed(ActionErrorCodes.Unavailable, $"File not found: {fullPath}");
		}

		try
		{
			Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true })?.Dispose();
			return ActionResult.Success();
		}
		catch (Exception ex)
		{
			return ActionResult.Failed(ActionErrorCodes.Unavailable, $"Could not open {fullPath}: {ex.Message}");
		}
	}

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context) =>
			OpenRecentFileAsync(session, 0, context.CancellationToken);
	}
}
