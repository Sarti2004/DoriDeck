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

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOs;

	/// predefined Commands
	private static readonly IReadOnlyList<string> PredefinedCommands =
	[
		.. new[] { "pp", "p", "mp", "mf", "f", "ff" }
			.Select(d => $"NoteInput.CreateDynamic?Definition={d}"),
		.. new[] { "ped", "pizz", "marcato", "portato", "mute", "upbow", "downbow", "con sord", "nh", "Vh", "arco"}
			.Select(t => $"NoteInput.CreatePlayingTechnique?Definition={t}&UseLocalOverride=0"),
		.. new[] { "gliss", "trill", "mordent" }
			.Select(o => $"NoteInput.CreateOrnament?Definition={o}&UseLocalOverride=0"),
		.. new[] { "kAccent", "kStaccato", "kMarcato", "kTenuto", "kStress", "kStaccatissimo", "kUnstress" }
			.Select(a => $"NoteInput.SetArticulation?Value={a}"),
		.. new[] { "fermata", "caesura" }
			.Select(a => $"NoteInput.CreatePause?Definition={a}"),
	];

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

		// Predefined entries already carry parameters, so match them against the full typed text.
		var predefined = PredefinedCommands.Where(command =>
			string.IsNullOrWhiteSpace(enteredValue) ||
			command.Contains(enteredValue, StringComparison.OrdinalIgnoreCase));

		var options = predefined
			.Concat(commands
				.OrderBy(command => command.DisplayName ?? command.Name, StringComparer.OrdinalIgnoreCase)
				.Select(command => command.Name + parameterSuffix))
			.Distinct(StringComparer.Ordinal)
			// Label must equal Value: when they differ, Macro Deck's combobox switches
			// to picker mode and ignores typed text, so '?' arguments can't be added.
			.Select(value => new ActionParameterOption
			{
				// Preserve any parameters already typed after '?' when the user selects a suggestion.
				Value = value,
				Label = value,
			})
			.ToList();

		// Macro Deck flags a required value that isn't one of the options as
		// "Configuration no longer available", so echo the typed value back as an option.
		if (!string.IsNullOrWhiteSpace(enteredValue) &&
			!options.Any(option => string.Equals(option.Value, enteredValue, StringComparison.Ordinal)))
		{
			options.Insert(0, new ActionParameterOption
			{
				Value = enteredValue,
				Label = enteredValue,
			});
		}

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
