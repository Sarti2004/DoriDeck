using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using ScoreInterface.Commands;

namespace DoriDeck.Actions;

/// Runs a single Dorico command
internal sealed class RunCommandAction(DoricoSession session) :
	DoriDeckActionBase(session),
	IActionDefinition,
	IDynamicOptionsActionDefinition
{
	private const string CommandNameParameter = "commandName";
	private const string ApplyToAllFlowsParameter = "applyToAllFlows";

	public string Id => "run-command";

	public LocalizedText Name => Strings.Actions.RunCommand.Name();

	public LocalizedText Description => Strings.Actions.RunCommand.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Autocomplete(
			CommandNameParameter,
			label: Strings.Actions.RunCommand.CommandName.Label(),
			description: Strings.Actions.RunCommand.CommandName.Description(),
			placeholder: Strings.Actions.RunCommand.CommandName.Placeholder(),
			required: true),
		ActionParameter.Toggle(
			ApplyToAllFlowsParameter,
			label: Strings.Actions.RunCommand.ApplyToAllFlows.Label(),
			description: Strings.Actions.RunCommand.ApplyToAllFlows.Description(),
			defaultValue: false),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOS;

	public Task<DynamicOptionsResult> GetDynamicOptionsAsync(
		DynamicOptionsContext context,
		CancellationToken cancellationToken)
	{
		if (context.ParameterName != CommandNameParameter)
		{
			return Task.FromResult(new DynamicOptionsResult
			{
				Options = [],
			});
		}

		cancellationToken.ThrowIfCancellationRequested();

		var enteredValue = context.Filter?.Trim() ?? string.Empty;
		var separatorIndex = enteredValue.IndexOf('?');
		var filter = separatorIndex >= 0
			? enteredValue[..separatorIndex]
			: enteredValue;
		var parameterSuffix = separatorIndex >= 0
			? enteredValue[separatorIndex..]
			: string.Empty;

		var commands = Session.AvailableCommands.AsEnumerable();

		if (!string.IsNullOrWhiteSpace(filter))
		{
			commands = commands.Where(command =>
				command.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
				(!string.IsNullOrWhiteSpace(command.DisplayName) &&
				 command.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase)));
		}

		var options = commands
			.OrderBy(command => command.DisplayName ?? command.Name, StringComparer.OrdinalIgnoreCase)
			.Select(command => new ActionParameterOption
			{
				// Macro Deck displays Label and stores Value. Preserve any parameters
				// already typed after '?' when the user selects a suggestion.
				Value = command.Name + parameterSuffix,
				Label = string.IsNullOrWhiteSpace(command.DisplayName)
					? command.Name
					: $"{command.DisplayName} ({command.Name})",
			})
			.ToArray();

		return Task.FromResult(new DynamicOptionsResult
		{
			Options = options,
			AllowsCustomValue = true,
			CacheSeconds = 30,
		});
	}

	public IActionExecutor CreateExecutor() => new Executor(Session);

	private sealed class Executor(DoricoSession session) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var commandName = context.Parameters.TryGetValue(CommandNameParameter, out var name)
				? name?.ToString()
				: null;

			if (string.IsNullOrWhiteSpace(commandName))
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required(Strings.Actions.RunCommand.CommandName.Label()));
			}

			commandName = await session.ResolveVariablesAsync(commandName);

			var dorico = await GetConnectedRemoteAsync(session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			var applyToAllFlows = context.Parameters.TryGetValue(ApplyToAllFlowsParameter, out var apply) &&
				apply is bool applyBool && applyBool;

			if (applyToAllFlows && session.FlowsCount > 1)
			{
				for (var flowId = 0; flowId < session.FlowsCount; flowId++)
				{
					await dorico.SendRequestAsync(
						new Command("Edit.GoToFlow", new CommandParameter("FlowID", flowId.ToString(CultureInfo.InvariantCulture))),
						context.CancellationToken);
					await Task.Delay(session.FlowSwitchDelay, context.CancellationToken);

					await RunWithModeSwitchAsync(dorico, commandName, context.CancellationToken);
				}
			}
			else
			{
				await RunWithModeSwitchAsync(dorico, commandName, context.CancellationToken);
			}

			return ActionResult.Success();
		}
	}
}
