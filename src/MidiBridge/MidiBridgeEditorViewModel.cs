using System.Globalization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Ui.Runtime;

namespace DoriDeck.MidiBridge;

internal sealed class MidiBridgeEditorViewModel
{
	public const string NoIcon = "none";

	private readonly MidiBridgeStore _store;
	private readonly MidiRouterClient _router;

	public MidiBridgeEditorViewModel(
		MidiBridgeStore store,
		MidiRouterClient router,
		MidiBridgeActions actions)
	{
		_store = store;
		_router = router;
		Actions = actions;

		foreach (var action in actions.All)
		{
			foreach (var parameter in action.Parameters)
			{
				var key = ParameterKey(action.Id, parameter.Name);
				switch (parameter.Type)
				{
					case ActionParameterType.Boolean:
						Toggles[key] = new UiState<bool>(false);
						break;
					case ActionParameterType.Number:
						Numbers[key] = new UiState<double>(0);
						break;
					default:
						Texts[key] = new UiState<string>(string.Empty);
						break;
				}
			}
		}

		Bindings = new UiState<IReadOnlyDictionary<int, MidiBinding>>(store.Bindings);
		Mode = new UiState<string>(store.Mode);
		Router = new UiState<RouterStatus>(router.Status);
		_store.Changed += OnStoreChanged;
		_router.StatusChanged += OnRouterChanged;
	}

	public MidiBridgeActions Actions { get; }

	public UiState<int> Octaves { get; } = new(MidiKeyboardModel.DefaultOctaveCount);

	public UiState<int> Shift { get; } = new(0);

	public UiState<int?> Selected { get; } = new(null);

	public UiState<IReadOnlyDictionary<int, MidiBinding>> Bindings { get; }

	public UiState<string> Mode { get; }

	public UiState<RouterStatus> Router { get; }

	public UiState<string> ActionId { get; } = new(MidiBridgeActions.DefaultActionId);

	public UiState<string> Icon { get; } = new(NoIcon);

	public UiState<bool> MissingRequired { get; } = new(false);

	public Dictionary<string, UiState<string>> Texts { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, UiState<bool>> Toggles { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, UiState<double>> Numbers { get; } = new(StringComparer.Ordinal);

	public static string ParameterKey(string actionId, string parameterName) => $"{actionId}/{parameterName}";

	public bool IsMapped(int note) => Bindings.Value.ContainsKey(note);

	public IReadOnlyList<string> ModeOptions()
	{
		var modes = new List<string>(Router.Value.State?.Modes ?? []);
		foreach (var mode in new[] { Mode.Value, MidiBridgeDocument.DefaultMode })
		{
			if (!modes.Contains(mode, StringComparer.Ordinal))
			{
				modes.Add(mode);
			}
		}

		return modes;
	}

	public void SetMode(string mode)
	{
		Mode.Set(mode);
		_ = _store.SetModeAsync(mode);
	}

	public void Select(int note)
	{
		Selected.Set(note);
		MissingRequired.Set(false);
		LoadDraft(Bindings.Peek().GetValueOrDefault(note));
	}

	public async Task SaveAsync()
	{
		if (Selected.Peek() is not { } note || !Actions.ById.TryGetValue(ActionId.Peek(), out var action))
		{
			return;
		}

		var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var parameter in action.Parameters)
		{
			var key = ParameterKey(action.Id, parameter.Name);
			object? value = parameter.Type switch
			{
				ActionParameterType.Boolean => Toggles[key].Peek(),
				ActionParameterType.Number => Numbers[key].Peek(),
				_ => Texts[key].Peek().Trim() is { Length: > 0 } text ? text : null,
			};

			if (parameter.Required && value is null)
			{
				MissingRequired.Set(true);
				return;
			}

			parameters[parameter.Name] = value;
		}

		MissingRequired.Set(false);
		var icon = Icon.Peek();
		await _store.SetAsync(new MidiBinding
		{
			Note = note,
			ActionId = action.Id,
			Parameters = parameters,
			Icon = icon == NoIcon ? null : icon,
		}).ConfigureAwait(false);
	}

	public async Task ClearAsync()
	{
		if (Selected.Peek() is not { } note)
		{
			return;
		}

		MissingRequired.Set(false);
		await _store.RemoveAsync(note).ConfigureAwait(false);
		LoadDraft(null);
	}

	public void Detach()
	{
		_store.Changed -= OnStoreChanged;
		_router.StatusChanged -= OnRouterChanged;
	}

	private void LoadDraft(MidiBinding? binding)
	{
		var actionId = binding is not null && Actions.ById.ContainsKey(binding.ActionId)
			? binding.ActionId
			: MidiBridgeActions.DefaultActionId;

		foreach (var action in Actions.All)
		{
			foreach (var parameter in action.Parameters)
			{
				var key = ParameterKey(action.Id, parameter.Name);
				var value = action.Id == actionId && binding?.Parameters.TryGetValue(parameter.Name, out var stored) == true
					? stored
					: parameter.DefaultValue;

				switch (parameter.Type)
				{
					case ActionParameterType.Boolean:
						Toggles[key].Set(value is true || (value is string text && bool.TryParse(text, out var parsed) && parsed));
						break;
					case ActionParameterType.Number:
						Numbers[key].Set(value is null ? 0 : Convert.ToDouble(value, CultureInfo.InvariantCulture));
						break;
					default:
						Texts[key].Set(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
						break;
				}
			}
		}

		ActionId.Set(actionId);
		Icon.Set(binding?.Icon ?? NoIcon);
	}

	private void OnStoreChanged()
	{
		Bindings.Set(_store.Bindings);
		Mode.Set(_store.Mode);
	}

	private void OnRouterChanged() => Router.Set(_router.Status);
}
