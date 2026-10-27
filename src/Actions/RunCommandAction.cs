using System.Globalization;
using System.Text.Json;
using DoriDeck.PianoFolder.Piano;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using ScoreInterface.Commands;

namespace DoriDeck.Actions;

/// Runs a single Dorico command
internal sealed class RunCommandAction(DoricoSession session) :
	DoriDeckActionBase(session),
	IActionDefinition,
	IDynamicOptionsActionDefinition,
	IUiConfigurableActionDefinition
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
	private static readonly IReadOnlyList<(string Command, string Name)> PredefinedCommands =
	[
		.. new[] { "pp", "p", "mp", "mf", "f", "ff" }
			.Select(d => ($"NoteInput.CreateDynamic?Definition={d}", $"Dynamic {d}")),
		.. new[]
		{
			("ped", "Pedal"), ("pizz", "Pizzicato"), ("marcato", "Marcato"), ("portato", "Portato"),
			("mute", "Mute"), ("upbow", "Up-bow"), ("downbow", "Down-bow"), ("con sord", "Con sordino"),
			("nh", "Natural harmonic"), ("Vh", "Harmonic"), ("arco", "Arco"),
		}
			.Select(t => ($"NoteInput.CreatePlayingTechnique?Definition={t.Item1}&UseLocalOverride=0", t.Item2)),
		.. new[] { ("gliss", "Glissando"), ("trill", "Trill"), ("mordent", "Mordent") }
			.Select(o => ($"NoteInput.CreateOrnament?Definition={o.Item1}&UseLocalOverride=0", o.Item2)),
		.. new[]
		{
			("kAccent", "Accent"), ("kStaccato", "Staccato"), ("kMarcato", "Marcato (articulation)"),
			("kTenuto", "Tenuto"), ("kStress", "Stress"), ("kStaccatissimo", "Staccatissimo"), ("kUnstress", "Unstress"),
		}
			.Select(a => ($"NoteInput.SetArticulation?Value={a.Item1}", a.Item2)),
		.. new[] { ("fermata", "Fermata"), ("caesura", "Caesura") }
			.Select(p => ($"NoteInput.CreatePause?Definition={p.Item1}", p.Item2)),
		.. new[]
		{
			("kBreve", "Breve"), ("kSemibreve", "Whole note"), ("kMinim", "Half note"), ("kCrotchet", "Quarter note"),
			("kQuaver", "Eighth note"), ("kSemiquaver", "16th note"), ("kDemisemiquaver", "32nd note"),
			("kHemidemisemiquaver", "64th note"),
		}
			.Select(l => ($"NoteInput.NoteValue?LogDuration={l.Item1}&Set=true", l.Item2)),
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
		var predefined = PredefinedCommands.Select(predefinedCommand => predefinedCommand.Command).Where(command =>
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

	/// Custom configuration form. 
	public Task<IUiSession?> CreateConfigurationSessionAsync(
		ActionConfigurationRequest request,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		var commandName = new UiState<string>(
			request.Parameters.TryGetValue(CommandNameParameter, out var nameElement) &&
			nameElement.ValueKind == JsonValueKind.String
				? nameElement.GetString() ?? string.Empty
				: string.Empty);
		var applyToAllFlows = new UiState<bool>(
			request.Parameters.TryGetValue(ApplyToAllFlowsParameter, out var applyElement) &&
			applyElement.ValueKind == JsonValueKind.True);

		ActionConfigurationSession? configurationSession = null;

		var root = new UiConfigStack
		{
			Key = "run-command-config",
			Children =
			[
				new UiAutocompleteInput
				{
					Key = CommandNameParameter,
					Binding = Bind.Custom(
						() => commandName.Value,
						value =>
						{
							commandName.Set(value ?? string.Empty);
							configurationSession?.Report(CommandNameParameter, JsonSerializer.SerializeToElement(commandName.Value));
						}),
					Label = Strings.Actions.RunCommand.CommandName.Label(),
					Description = Strings.Actions.RunCommand.CommandName.Description(),
					Placeholder = Strings.Actions.RunCommand.CommandName.Placeholder(),
					Required = true,
					AllowsCustomValue = true,
					Options = UiValue.Of(BuildCommandOptions()),
				},
				new UiBooleanInput
				{
					Key = ApplyToAllFlowsParameter,
					Binding = Bind.Custom(
						() => applyToAllFlows.Value,
						value =>
						{
							applyToAllFlows.Set(value);
							configurationSession?.Report(ApplyToAllFlowsParameter, JsonSerializer.SerializeToElement(value));
						}),
					Label = Strings.Actions.RunCommand.ApplyToAllFlows.Label(),
					Description = Strings.Actions.RunCommand.ApplyToAllFlows.Description(),
				},
			],
		};

		configurationSession = new ActionConfigurationSession(
			new PianoKeyboardSession(new UiView(request.Session.Surface, root)));

		return Task.FromResult<IUiSession?>(configurationSession);
	}

	private IReadOnlyList<UiOption> BuildCommandOptions() =>
	[
		.. PredefinedCommands.Select(predefined => new UiOption
		{
			Value = predefined.Command,
			Label = $"{predefined.Name} — {predefined.Command}",
		}),
		.. Session.AvailableCommands
			.OrderBy(command => command.DisplayName ?? command.Name, StringComparer.OrdinalIgnoreCase)
			// The dropdown renders only the label, so show both names; the editor filters by label and value.
			.Select(command => new UiOption
			{
				Value = command.Name,
				Label = string.IsNullOrWhiteSpace(command.DisplayName)
					? command.Name
					: $"{command.DisplayName} — {command.Name}",
			}),
	];

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
