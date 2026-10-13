using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace DoriDeck.Actions;

/// <summary>
/// Opens the second project in Dorico's recent file list.
/// </summary>
internal sealed class OpenPreviousFileAction(DoricoSession session) : DoriDeckActionBase(session), IActionDefinition
{
	public string Id => "open-previous-file";

	public LocalizedText Name => "Open Previous File";

	public LocalizedText Description => "Opens the second project in Dorico's recent file list.";

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session);

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public Task<ActionResult> ExecuteAsync(ActionExecutionContext context) =>
			OpenRecentFileAction.OpenRecentFileAsync(session, 1, context.CancellationToken);
	}
}
