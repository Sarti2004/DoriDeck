using System.Globalization;
using DoriDeck.PianoFolder.Piano;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;

namespace DoriDeck.MidiBridge;

internal static class MidiBridgeEditorView
{
	// wouldn't load otherwise(tree size limit exceeded)
	private const int MaxSuggestions = 50;

	private static readonly IReadOnlyList<UiOption> OctaveOptions =
	[
		.. Enumerable
			.Range(MidiKeyboardModel.MinOctaveCount, MidiKeyboardModel.MaxOctaveCount)
			.Select(octaves => UiOption.Of(octaves.ToString(CultureInfo.InvariantCulture))),
	];

	private static readonly IReadOnlyList<UiOption> ShiftOptions =
	[
		.. Enumerable
			.Range(
				MidiKeyboardModel.MinShift,
				MidiKeyboardModel.MaxShift - MidiKeyboardModel.MinShift + 1)
			.Select(shift => UiOption.Of(
				shift.ToString(CultureInfo.InvariantCulture),
				LocalizedText.FromLiteral(shift > 0 ? $"+{shift}" : shift.ToString(CultureInfo.InvariantCulture)))),
	];

	private static readonly IReadOnlyList<UiOption> IconOptions =
	[
		new UiOption { Value = MidiBridgeEditorViewModel.NoIcon, Label = Strings.MidiBridge.Editor.Icon.None() },
		.. UiIcons.WellKnown.Select(icon => new UiOption { Value = icon, Label = LocalizedText.FromLiteral(icon), Icon = icon }),
	];

	public static UiElement Build(MidiBridgeEditorViewModel model) =>
		new UiConfigStack
		{
			Key = "mb",
			Children =
			[
				RouterStatus(model),
				new UiChoiceInput
				{
					Key = "mode",
					Label = Strings.MidiBridge.Editor.Mode.Label(),
					Description = Strings.MidiBridge.Editor.Mode.Description(),
					Segmented = true,
					Options = UiValue.From<IReadOnlyList<UiOption>>(() => [.. model.ModeOptions().Select(UiOption.Of)]),
					Binding = Bind.Custom(() => model.Mode.Value, model.SetMode),
				},
				new UiConfigStack
				{
					Key = "range",
					Direction = UiComponentDirections.Horizontal,
					Children =
					[
						IntChoice("octaves", Strings.MidiBridge.Editor.Octaves.Label(), OctaveOptions, model.Octaves, MidiKeyboardModel.ClampOctaveCount),
						IntChoice("shift", Strings.MidiBridge.Editor.Shift.Label(), ShiftOptions, model.Shift, MidiKeyboardModel.ClampShift),
					],
				},
				MidiKeyboardView.Build(model),
				new UiWhen
				{
					Key = "hint-when",
					Condition = () => model.Selected.Value is null,
					Content = () => new UiProse { Key = "hint", Text = Strings.MidiBridge.Editor.SelectKeyHint() },
				},
				new UiWhen
				{
					Key = "editor-when",
					Condition = () => model.Selected.Value is not null,
					Content = () => Editor(model),
				},
			],
		};

	private static UiElement RouterStatus(MidiBridgeEditorViewModel model) =>
		new UiFragment
		{
			Key = "router",
			Children =
			[
				new UiWhen
				{
					Key = "unavailable-when",
					Condition = () => !model.Router.Value.Available,
					Content = () => new UiBanner
					{
						Key = "unavailable",
						Severity = "warning",
						Text = Strings.MidiBridge.Editor.RouterUnavailable(),
					},
				},
				new UiWhen
				{
					Key = "status-when",
					Condition = () => model.Router.Value is { Available: true, State: not null },
					Content = () => new UiStatus
					{
						Key = "status",
						Icon = UiIcons.MusicNote,
						Label = Strings.MidiBridge.Editor.RouterLabel(),
						Value = UiText.Optional(() => model.Router.Value.State is { } state
							? Strings.MidiBridge.Editor.RouterState(state.PendingMode ?? state.Mode ?? "?", state.Status ?? string.Empty)
							: UiText.None()),
					},
				},
				new UiWhen
				{
					Key = "error-when",
					Condition = () => model.Router.Value.Error is not null,
					Content = () => new UiBanner
					{
						Key = "error",
						Severity = "error",
						Text = UiText.Optional(() => model.Router.Value.Error is { } error
							? Strings.MidiBridge.Editor.RouterError(error)
							: UiText.None()),
					},
				},
			],
		};

	private static UiElement IntChoice(
		string key,
		LocalizedText label,
		IReadOnlyList<UiOption> options,
		MacroDeck.Ui.Runtime.UiState<int> state,
		Func<int, int> clamp) =>
		new UiChoiceInput
		{
			Key = key,
			Label = label,
			Segmented = true,
			Options = UiValue.Of(options),
			Binding = Bind.Custom(
				() => state.Value.ToString(CultureInfo.InvariantCulture),
				value =>
				{
					if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
					{
						state.Set(clamp(number));
					}
				}),
		};

	private static UiElement Editor(MidiBridgeEditorViewModel model) =>
		new UiConfigStack
		{
			Key = "editor",
			Children =
			[
				new UiHeading
				{
					Key = "selected",
					Text = UiText.Optional(() => model.Selected.Value is { } note
						? Strings.MidiBridge.Editor.SelectedKey(MidiBridgeSerializer.NoteName(note), note.ToString(CultureInfo.InvariantCulture))
						: UiText.None()),
					Badge = UiText.Optional(() => model.Selected.Value is { } note && model.IsMapped(note)
						? Strings.MidiBridge.Editor.Assigned()
						: UiText.None()),
				},
				new UiChoiceInput
				{
					Key = "action",
					Label = Strings.MidiBridge.Editor.Action.Label(),
					Searchable = true,
					Required = true,
					Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[.. model.Actions.All.Select(action => UiOption.Of(action.Id, action.Name))]),
					Binding = Bind.To(model.ActionId),
				},
				.. model.Actions.All
					.Where(action => action.Parameters.Count > 0)
					.Select(action => new UiWhen
					{
						Key = $"{action.Id}-when",
						Condition = () => model.ActionId.Value == action.Id,
						Content = () => new UiFragment
						{
							Key = $"{action.Id}-parameters",
							Children = [.. action.Parameters.Select(parameter => Parameter(model, action, parameter))],
						},
					}),
				new UiChoiceInput
				{
					Key = "icon",
					Label = Strings.MidiBridge.Editor.Icon.Label(),
					Description = Strings.MidiBridge.Editor.Icon.Description(),
					Searchable = true,
					Options = UiValue.Of(IconOptions),
					Binding = Bind.To(model.Icon),
				},
				new UiWhen
				{
					Key = "missing-when",
					Condition = () => model.MissingRequired.Value,
					Content = () => new UiBanner
					{
						Key = "missing",
						Severity = "error",
						Text = Strings.MidiBridge.Editor.MissingRequired(),
					},
				},
				new UiConfigStack
				{
					Key = "buttons",
					Direction = UiComponentDirections.Horizontal,
					Children =
					[
						new UiConfigButton
						{
							Key = "save",
							Label = Strings.MidiBridge.Editor.Save(),
							Icon = UiIcons.Check,
							IconWithLabel = true,
							Variant = UiButtonVariants.Primary,
							Events = [UiEventHandler.OnAsync(UiConfigEvents.Activate, _ => model.SaveAsync())],
						},
						new UiWhen
						{
							Key = "clear-when",
							Condition = () => model.Selected.Value is { } note && model.IsMapped(note),
							Content = () => new UiConfigButton
							{
								Key = "clear",
								Label = Strings.MidiBridge.Editor.Clear(),
								Icon = UiIcons.Trash,
								IconWithLabel = true,
								Variant = UiButtonVariants.Danger,
								Events = [UiEventHandler.OnAsync(UiConfigEvents.Activate, _ => model.ClearAsync())],
							},
						},
					],
				},
			],
		};

	private static UiElement Parameter(MidiBridgeEditorViewModel model, IActionDefinition action, ActionParameter parameter)
	{
		var key = MidiBridgeEditorViewModel.ParameterKey(action.Id, parameter.Name);
		var label = Text(parameter.Label);
		var description = Text(parameter.Description);

		return parameter.Type switch
		{
			ActionParameterType.Boolean => new UiBooleanInput
			{
				Key = parameter.Name,
				Label = label,
				Description = description,
				Binding = Bind.To(model.Toggles[key]),
			},
			ActionParameterType.Number => new UiNumberInput
			{
				Key = parameter.Name,
				Label = label,
				Description = description,
				Required = parameter.Required,
				Min = parameter.Min is { } min ? min : UiValue.None<double>(),
				Max = parameter.Max is { } max ? max : UiValue.None<double>(),
				Step = parameter.Step is { } step ? step : UiValue.None<double>(),
				Binding = Bind.To(model.Numbers[key]),
			},
			ActionParameterType.Choice => new UiChoiceInput
			{
				Key = parameter.Name,
				Label = label,
				Description = description,
				Required = parameter.Required,
				Searchable = parameter.Searchable,
				Options = UiValue.Of<IReadOnlyList<UiOption>>(
					[.. (parameter.Options ?? []).Select(option => UiOption.Of(option.Value, option.Label))]),
				Binding = Bind.To(model.Texts[key]),
			},
			ActionParameterType.Autocomplete => Autocomplete(model, action, parameter, key, label, description),
			_ => new UiStringInput
			{
				Key = parameter.Name,
				Label = label,
				Description = description,
				Placeholder = Text(parameter.Placeholder),
				Required = parameter.Required,
				Multiline = parameter.Multiline,
				Binding = Bind.To(model.Texts[key]),
			},
		};
	}

	private static UiAutocompleteInput Autocomplete(
		MidiBridgeEditorViewModel model,
		IActionDefinition action,
		ActionParameter parameter,
		string key,
		UiText label,
		UiText description)
	{
		var options = new UiOptionsState(UiOptionSource.From(
			(query, cancellationToken) => LoadOptionsAsync(action, parameter, query, cancellationToken)));
		options.Reload();

		return new UiAutocompleteInput
		{
			Key = parameter.Name,
			Label = label,
			Description = description,
			Placeholder = Text(parameter.Placeholder),
			Required = parameter.Required,
			OptionsState = options,
			Binding = Bind.To(model.Texts[key]),
			Events =
			[
				UiEventHandler.On(UiConfigEvents.Filter, data =>
				{
					options.SetFilter(data.TryGetString(out var filter) ? filter : null);
					options.Reload();
				}),
				UiEventHandler.On(UiConfigEvents.Reload, options.Reload),
			],
		};
	}

	private static async Task<UiOptionResult> LoadOptionsAsync(
		IActionDefinition action,
		ActionParameter parameter,
		UiOptionQuery query,
		CancellationToken cancellationToken)
	{
		IReadOnlyList<ActionParameterOption> options = parameter.Options ?? [];
		if (action is IDynamicOptionsActionDefinition dynamic)
		{
			var result = await dynamic.GetDynamicOptionsAsync(
				new DynamicOptionsContext
				{
					ParameterName = parameter.Name,
					Filter = query.Filter,
					CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal),
				},
				cancellationToken).ConfigureAwait(false);
			options = result.Options;
		}

		return UiOptionResult.From([.. options.Take(MaxSuggestions).Select(option => UiOption.Of(option.Value, option.Label))]);
	}

	private static UiText Text(LocalizedText text) => text.IsEmpty ? UiText.None() : text;
}
