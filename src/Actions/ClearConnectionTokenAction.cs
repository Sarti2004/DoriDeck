using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace DoriDeck.Actions;

/// <summary>
/// Clears the connection token for Dorico. Use to force a re-authentication.
/// </summary>
internal sealed class ClearConnectionTokenAction (DoricoSession session) : DoriDeckActionBase(session), IActionDefinition
{
	public string Id => "clear-connection-token";

	public LocalizedText Name => "Clear Connection Token";

	public LocalizedText Description => "Clears the connection token for Dorico. Use to force a re-authentication.";

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session);

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				// The host may not answer config writes promptly (e.g. while draining is paused); the write stays queued
				// and lands once it resumes, so don't hold the action's reply on it.
				await session.ClearSessionTokenAsync().WaitAsync(HostCallWaitLimit, context.CancellationToken);
			}
			catch (TimeoutException)
			{
			}

			return ActionResult.Success();
		}
	}
}
