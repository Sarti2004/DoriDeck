using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface.Commands;

namespace DoriDeck.Actions;


internal sealed class PickupMeasureAction(DoricoSession session) : DoriDeckActionBase(session), IActionDefinition
{
	private static readonly TimeSpan StepDelay = TimeSpan.FromMilliseconds(20);

	public string Id => "pickup-measure";

	public LocalizedText Name => Strings.Actions.PickupMeasure.Name();

	public LocalizedText Description => Strings.Actions.PickupMeasure.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } = [];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session);

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			var token = context.CancellationToken;

			await dorico.SendRequestAsync(new Command("Edit.SelectToStartOfSystem"), token);
			await Task.Delay(StepDelay, token);
			await dorico.SendRequestAsync(new Command("NoteInput.InsertScope", new CommandParameter("InsertModeScope", "kGlobalCurrentBar")), token);
			await Task.Delay(StepDelay, token);
			await dorico.SendRequestAsync(new Command("Filter.Rests"), token);
			await Task.Delay(StepDelay, token);
			await dorico.SendRequestAsync(new Command("Edit.Delete"), token);
			await Task.Delay(StepDelay, token);
			await dorico.SendRequestAsync(new Command("NoteInput.Mode"), token);

			return ActionResult.Success();
		}
	}
}
