using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface.Commands;

namespace DoriDeck.Actions;

/// Starts a tuplet run at the given ratio, need to test and improve implementation
internal sealed class TupletAction(DoricoSession session) : DoriDeckActionBase(session), IActionDefinition
{
	private const string RatioParameter = "ratio";

	public string Id => "tuplet";

	public LocalizedText Name => Strings.Actions.Tuplet.Name();

	public LocalizedText Description => Strings.Actions.Tuplet.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(
			RatioParameter,
			label: Strings.Actions.Tuplet.Ratio.Label(),
			description: Strings.Actions.Tuplet.Ratio.Description(),
			placeholder: "3",
			required: false),
	];

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

			if (session.TupletMode)
			{
				await dorico.SendRequestAsync(new Command("NoteInput.EndTupletRun"), context.CancellationToken);
				session.TupletMode = false;
				return ActionResult.Success();
			}

			var ratio = context.Parameters.TryGetValue(RatioParameter, out var ratioValue) ? ratioValue?.ToString() : null;
			CommandParameter[] parameters = string.IsNullOrEmpty(ratio)
				? []
				: [new CommandParameter("Definition", ratio.Replace(",", "\\\\,"))];

			await dorico.SendRequestAsync(new Command("NoteInput.StartTupletRun", parameters), context.CancellationToken);
			session.TupletMode = true;

			return ActionResult.Success();
		}
	}
}
