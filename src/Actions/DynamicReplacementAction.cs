using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using Serilog;

namespace DoriDeck.Actions;

/// <summary>
/// Walks forward through dynamics, playing techniques and system text starting at the current
/// selection, replacing every entry that exactly matches <c>find</c> (e.g. mp to mf).
/// </summary>
internal sealed class DynamicReplacementAction(DoricoSession session, ILogger logger)
	: DoriDeckActionBase(session), IActionDefinition
{
	private const string FindParameter = "find";
	private const string ReplaceParameter = "replace";

	private readonly ILogger _logger = logger.ForContext<DynamicReplacementAction>();

	public string Id => "dynamic-replacement";

	public LocalizedText Name => Strings.Actions.DynamicReplacement.Name();

	public LocalizedText Description => Strings.Actions.DynamicReplacement.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Text(
			FindParameter,
			label: Strings.Actions.DynamicReplacement.Find.Label(),
			description: Strings.Actions.DynamicReplacement.Find.Description(),
			placeholder: Strings.Actions.DynamicReplacement.Find.Placeholder(),
			required: true),
		ActionParameter.Text(
			ReplaceParameter,
			label: Strings.Actions.DynamicReplacement.Replace.Label(),
			description: Strings.Actions.DynamicReplacement.Replace.Description(),
			placeholder: Strings.Actions.DynamicReplacement.Replace.Placeholder(),
			required: false),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public IActionExecutor CreateExecutor() => new Executor(Session, _logger);

	private sealed class Executor(DoricoSession session, ILogger logger) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var find = context.Parameters.TryGetValue(FindParameter, out var findValue) ? findValue?.ToString() : null;
			if (string.IsNullOrWhiteSpace(find))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required(Strings.Actions.DynamicReplacement.Find.Label()));
			}

			var replace = context.Parameters.TryGetValue(ReplaceParameter, out var replaceValue)
				? replaceValue?.ToString() ?? string.Empty
				: string.Empty;

			try
			{
				var result = await session.DynamicReplacementWalker.RunAsync(find, replace, context.CancellationToken);

				logger.Information(
					"Dynamic replacement complete. Visited: {Visited}; changed: {Changed}; stop reason: {StopReason}",
					result.Visited,
					result.Changed,
					result.StopReason);

				return ActionResult.Success();
			}
			catch (InvalidOperationException ex)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, ex.Message);
			}
		}
	}
}
