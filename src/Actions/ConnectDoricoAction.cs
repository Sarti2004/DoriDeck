using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace DoriDeck.Actions;

/// <summary>
/// Connects to Dorico.
/// </summary>
internal sealed class ConnectDoricoAction(DoricoSession session) : DoriDeckActionBase(session), IActionDefinition
{
	public string Id => "connect-dorico";

	public LocalizedText Name => Strings.Actions.ConnectDorico.Name();

	public LocalizedText Description => Strings.Actions.ConnectDorico.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session);

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);

			return dorico != null
				? ActionResult.Success()
				: ActionResult.Failed(
					ActionErrorCodes.Unavailable,
					Strings.Actions.ConnectDorico.ConnectionFailed());
		}
	}
}
