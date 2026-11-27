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
using ScoreInterface;
using ScoreInterface.Exceptions;
using ScoreInterface.Responses;

namespace DoriDeck.Actions;

internal abstract class SetOptionAction(DoricoSession session) :
	DoriDeckActionBase(session),
	IActionDefinition,
	IUiConfigurableActionDefinition
{
	private const string NameParameter = "name";
	private const string ValueParameter = "value";
	private const string ValueTypeParameter = "valueType";
	private const string EnumValuesParameter = "enumValues";
	private const char EnumValuesSeparator = '|';
	private const int MaxSuggestions = 50;

	public abstract string Id { get; }

	public abstract LocalizedText Name { get; }

	public abstract LocalizedText Description { get; }

	private IReadOnlyList<ActionParameter>? _parameters;

	public IReadOnlyList<ActionParameter> Parameters => _parameters ??= [.. CommonParameters, .. ExtraParameters];

	protected virtual IReadOnlyList<ActionParameter> ExtraParameters => [];

	private static readonly IReadOnlyList<ActionParameter> CommonParameters =
	[
		ActionParameter.Autocomplete(
			NameParameter,
			label: Strings.Actions.SetOption.OptionName.Label(),
			description: Strings.Actions.SetOption.OptionName.Description(),
			placeholder: Strings.Actions.SetOption.OptionName.Placeholder(),
			required: true),
		ActionParameter.Autocomplete(
			ValueParameter,
			label: Strings.Actions.SetOption.OptionValue.Label(),
			description: Strings.Actions.SetOption.OptionValue.Description(),
			placeholder: Strings.Actions.SetOption.OptionValue.Placeholder(),
			required: true),
		ActionParameter.Text(
			ValueTypeParameter,
			label: Strings.Actions.SetOption.ValueType.Label(),
			description: Strings.Actions.SetOption.ValueType.Description(),
			required: false),
		ActionParameter.Text(
			EnumValuesParameter,
			label: Strings.Actions.SetOption.EnumValues.Label(),
			description: Strings.Actions.SetOption.EnumValues.Description(),
			required: false),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOs;

	protected abstract string OptionsType { get; }

	protected abstract Task<int?> GetLookupIdAsync(IScoreInterfaceRemote dorico, CancellationToken cancellationToken);

	protected abstract Task<IReadOnlyList<string>> BuildCommandsAsync(
		IScoreInterfaceRemote dorico,
		string dictionary,
		IReadOnlyDictionary<string, string> extras,
		CancellationToken cancellationToken);

	public Task<IUiSession?> CreateConfigurationSessionAsync(
		ActionConfigurationRequest request,
		CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();

		string Read(string key) =>
			request.Parameters.TryGetValue(key, out var element) && element.ValueKind == JsonValueKind.String
				? element.GetString() ?? string.Empty
				: string.Empty;

		var name = new UiState<string>(Read(NameParameter));
		var value = new UiState<string>(Read(ValueParameter));
		var optionsTask = LoadOptionsAsync();

		ActionConfigurationSession? configurationSession = null;

		void Report(string key, string text) =>
			configurationSession?.Report(key, JsonSerializer.SerializeToElement(text));

		async Task<OptionInfo?> FindAsync(string path)
		{
			var options = await optionsTask;
			return options.FirstOrDefault(option => string.Equals(option.Path, path.Trim(), StringComparison.Ordinal));
		}

		async Task StoreTypeAsync()
		{
			var options = await optionsTask;
			if (options.Count == 0)
			{
				return;
			}

			var info = await FindAsync(name.Value);
			Report(ValueTypeParameter, info?.ValueType ?? string.Empty);
			Report(EnumValuesParameter, string.Join(EnumValuesSeparator, info?.EnumValues ?? []));
		}

		var nameOptions = new UiOptionsState(UiOptionSource.From(async (query, _) =>
		{
			var filter = query.Filter?.Trim() ?? string.Empty;
			var options = await optionsTask;

			return UiOptionResult.From(
			[
				.. options
					.Where(option => filter.Length == 0 || option.Path.Contains(filter, StringComparison.OrdinalIgnoreCase))
					.Take(MaxSuggestions)
					.Select(option => new UiOption { Value = option.Path, Label = $"{option.Path} = {option.CurrentValue}" }),
			]) with
			{
				AllowsCustomValue = true,
			};
		}));
		nameOptions.Reload();

		var valueOptions = new UiOptionsState(UiOptionSource.From(async (query, _) =>
		{
			var filter = query.Filter?.Trim() ?? string.Empty;
			var info = await FindAsync(name.Value);
			IEnumerable<string> values = info?.ValueType == "kBool" ? ["true", "false"] : info?.EnumValues ?? [];

			return UiOptionResult.From(
			[
				.. values
					.Where(text => filter.Length == 0 || text.Contains(filter, StringComparison.OrdinalIgnoreCase))
					.Take(MaxSuggestions)
					.Select(text => new UiOption { Value = text, Label = text }),
			]) with
			{
				AllowsCustomValue = true,
			};
		}));
		valueOptions.Reload();

		var root = new UiConfigStack
		{
			Key = "set-option-config",
			Children =
			[
				new UiAutocompleteInput
				{
					Key = NameParameter,
					Binding = Bind.Custom(
						() => name.Value,
						text =>
						{
							name.Set(text ?? string.Empty);
							Report(NameParameter, name.Value);
							valueOptions.Reload();
							_ = StoreTypeAsync();
						}),
					Label = Strings.Actions.SetOption.OptionName.Label(),
					Description = Strings.Actions.SetOption.OptionName.Description(),
					Placeholder = Strings.Actions.SetOption.OptionName.Placeholder(),
					Required = true,
					AllowsCustomValue = true,
					OptionsState = nameOptions,
					Events =
					[
						UiEventHandler.On(UiConfigEvents.Filter, data =>
						{
							nameOptions.SetFilter(data.TryGetString(out var filter) ? filter : null);
							nameOptions.Reload();
						}),
						UiEventHandler.On(UiConfigEvents.Reload, nameOptions.Reload),
					],
				},
				new UiAutocompleteInput
				{
					Key = ValueParameter,
					Binding = Bind.Custom(
						() => value.Value,
						text =>
						{
							value.Set(text ?? string.Empty);
							Report(ValueParameter, value.Value);
						}),
					Label = Strings.Actions.SetOption.OptionValue.Label(),
					Description = Strings.Actions.SetOption.OptionValue.Description(),
					Placeholder = Strings.Actions.SetOption.OptionValue.Placeholder(),
					Required = true,
					AllowsCustomValue = true,
					OptionsState = valueOptions,
					Events =
					[
						UiEventHandler.On(UiConfigEvents.Filter, data =>
						{
							valueOptions.SetFilter(data.TryGetString(out var filter) ? filter : null);
							valueOptions.Reload();
						}),
						UiEventHandler.On(UiConfigEvents.Reload, valueOptions.Reload),
					],
				},
				.. ExtraParameters.Select(parameter =>
				{
					var state = new UiState<string>(Read(parameter.Name));
					return new UiStringInput
					{
						Key = parameter.Name,
						Binding = Bind.Custom(
							() => state.Value,
							text =>
							{
								state.Set(text ?? string.Empty);
								Report(parameter.Name, state.Value);
							}),
						Label = parameter.Label,
						Description = parameter.Description,
						Placeholder = parameter.Placeholder,
						Required = false,
					};
				}),
			],
		};

		configurationSession = new ActionConfigurationSession(
			new PianoKeyboardSession(new UiView(request.Session.Surface, root)));

		if (!string.IsNullOrWhiteSpace(name.Value))
		{
			_ = StoreTypeAsync();
		}

		return Task.FromResult<IUiSession?>(configurationSession);
	}

	private async Task<IReadOnlyList<OptionInfo>> LoadOptionsAsync()
	{
		try
		{
			var dorico = await GetConnectedRemoteAsync(Session, CancellationToken.None);
			if (dorico == null)
			{
				return [];
			}

			var id = await GetLookupIdAsync(dorico, CancellationToken.None);
			var options = await dorico.GetOptionsAsync(OptionsType, id);

			return [.. (options ?? []).OrderBy(option => option.Path, StringComparer.OrdinalIgnoreCase)];
		}
		catch (Exception)
		{			
			return [];
		}
	}

	private static string? FormatTypedValue(string valueType, IReadOnlyList<string> enumValues, string value)
	{
		switch (valueType)
		{
			case "kBool":
				return value.ToLowerInvariant() switch
				{
					"true" or "1" => "{bool: true}",
					"false" or "0" => "{bool: false}",
					_ => null,
				};

			case "kInt":
			case "kInt32":
				return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
					? $"{{int: {number.ToString(CultureInfo.InvariantCulture)}}}"
					: null;

			case "kEnum":
				// Dorico takes enums by their index; accept  the name or the index itself.
				var index = enumValues.ToList().FindIndex(name => string.Equals(name, value, StringComparison.OrdinalIgnoreCase));
				if (index < 0 && !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
				{
					return null;
				}

				return $"{{int: {index.ToString(CultureInfo.InvariantCulture)}}}";

			default:
				return $"{{string: \"{EscapeString(value)}\"}}";
		}
	}

	protected static string EscapeString(string value) => value.Replace(",", "\\,", StringComparison.Ordinal);

	public IActionExecutor CreateExecutor() => new Executor(this);

	private sealed class Executor(SetOptionAction action) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var name = (await ReadAsync(context, NameParameter)).Trim();
			if (name.Length == 0)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					MacroDeckStrings.Validation.Required(Strings.Actions.SetOption.OptionName.Label()));
			}

			var value = (await ReadAsync(context, ValueParameter)).Trim();
			var valueType = (await ReadAsync(context, ValueTypeParameter)).Trim();
			var enumValues = (await ReadAsync(context, EnumValuesParameter))
				.Split(EnumValuesSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

			var typedValue = FormatTypedValue(valueType, enumValues, value);
			if (typedValue == null)
			{
				return ActionResult.Failed(
					ActionErrorCodes.InvalidParameter,
					Strings.Actions.SetOption.InvalidValue(value, valueType));
			}

			var dorico = await GetConnectedRemoteAsync(action.Session, context.CancellationToken);
			if (dorico == null)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.NotConnected());
			}

			var dictionary = $"list: {{string: \"{EscapeString(name)}\"}}\\, {typedValue}";

			var extras = new Dictionary<string, string>(StringComparer.Ordinal);
			foreach (var parameter in action.ExtraParameters)
			{
				extras[parameter.Name] = (await ReadAsync(context, parameter.Name)).Trim();
			}

			try
			{
				foreach (var command in await action.BuildCommandsAsync(dorico, dictionary, extras, context.CancellationToken))
				{
					await dorico.SendCommandTextAsync(command, context.CancellationToken);
				}
			}
			catch (ScoreInterfaceException ex)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, ex.Message);
			}

			return ActionResult.Success();
		}

		private async Task<string> ReadAsync(ActionExecutionContext context, string parameter)
		{
			var text = context.Parameters.TryGetValue(parameter, out var raw) ? raw?.ToString() : null;
			return string.IsNullOrEmpty(text) ? string.Empty : await action.Session.ResolveVariablesAsync(text);
		}
	}
}

internal sealed class SetEngravingOptionAction(DoricoSession session) : SetOptionAction(session)
{
	public override string Id => "set-engraving-option";

	public override LocalizedText Name => Strings.Actions.SetEngravingOption.Name();

	public override LocalizedText Description => Strings.Actions.SetEngravingOption.Description();

	protected override string OptionsType => "kEngraving";

	protected override Task<int?> GetLookupIdAsync(IScoreInterfaceRemote dorico, CancellationToken cancellationToken) =>
		Task.FromResult<int?>(null);

	protected override Task<IReadOnlyList<string>> BuildCommandsAsync(
		IScoreInterfaceRemote dorico,
		string dictionary,
		IReadOnlyDictionary<string, string> extras,
		CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<string>>([$"Engrave.SetEngravingOptions?Dictionary={dictionary}"]);
}

internal sealed class SetNotationOptionAction(DoricoSession session) : SetOptionAction(session)
{
	public override string Id => "set-notation-option";

	public override LocalizedText Name => Strings.Actions.SetNotationOption.Name();

	public override LocalizedText Description => Strings.Actions.SetNotationOption.Description();

	protected override string OptionsType => "kNotation";

	protected override async Task<int?> GetLookupIdAsync(IScoreInterfaceRemote dorico, CancellationToken cancellationToken)
	{
		if (int.TryParse(Session.CurrentFlowId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var currentFlowId))
		{
			return currentFlowId;
		}

		var flows = await dorico.GetFlowsAsync(cancellationToken);
		return flows?.Flows?.Select(flow => (int?)flow.FlowID).FirstOrDefault();
	}

	protected override Task<IReadOnlyList<string>> BuildCommandsAsync(
		IScoreInterfaceRemote dorico,
		string dictionary,
		IReadOnlyDictionary<string, string> extras,
		CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<string>>([$"Project.Flow.SetNotationOptions?Dictionary={dictionary}"]);
}

internal sealed class SetLayoutOptionAction(DoricoSession session) : SetOptionAction(session)
{
	public override string Id => "set-layout-option";

	public override LocalizedText Name => Strings.Actions.SetLayoutOption.Name();

	public override LocalizedText Description => Strings.Actions.SetLayoutOption.Description();

	protected override string OptionsType => "kLayout";

	protected override async Task<int?> GetLookupIdAsync(IScoreInterfaceRemote dorico, CancellationToken cancellationToken)
	{
		var layouts = await dorico.GetLayoutsAsync(cancellationToken);
		return layouts?.Layouts?.Select(layout => (int?)layout.LayoutID).FirstOrDefault() ?? 0;
	}

	private const string LayoutIdsParameter = "layoutIds";

	protected override IReadOnlyList<ActionParameter> ExtraParameters { get; } =
	[
		ActionParameter.Text(
			LayoutIdsParameter,
			label: Strings.Actions.SetLayoutOption.LayoutIds.Label(),
			description: Strings.Actions.SetLayoutOption.LayoutIds.Description(),
			placeholder: Strings.Actions.SetLayoutOption.LayoutIds.Placeholder(),
			required: false),
	];

	
	protected override Task<IReadOnlyList<string>> BuildCommandsAsync(
		IScoreInterfaceRemote dorico,
		string dictionary,
		IReadOnlyDictionary<string, string> extras,
		CancellationToken cancellationToken)
	{
		var layoutIds = extras.TryGetValue(LayoutIdsParameter, out var ids) ? ids : string.Empty;
		var layoutPart = layoutIds.Length > 0 ? $"LayoutIDArray={EscapeString(layoutIds)}," : string.Empty;

		return Task.FromResult<IReadOnlyList<string>>([$"Project.Layout.SetOptions?{layoutPart}Dictionary={dictionary}"]);
	}
}
