using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace DoriDeck.Actions;

internal sealed class DynamicScriptAction(DoricoSession session, string id, string scriptName)
	: DoriDeckActionBase(session), IActionDefinition
{
	public string Id => id;

	public LocalizedText Name => Strings.Actions.DynamicScript.Name(scriptName);

	public LocalizedText Description => Strings.Actions.DynamicScript.Description(scriptName);

	public IReadOnlyList<ActionParameter> Parameters => [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session, scriptName);

	private sealed class Executor(DoricoSession session, string scriptName) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			await ScriptExecution.RunAsync(dorico, session.ScriptPath, scriptName, context.CancellationToken);
			return ActionResult.Success();
		}
	}
}
