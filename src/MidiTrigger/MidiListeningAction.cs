using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;

namespace DoriDeck.MidiTrigger;


// switch to enable/disable MiDI for Macrodeck (so dorico can use it on Windows)
internal sealed class MidiListeningAction(MidiInputListener listener, Func<bool, Task> saveEnabled) :
	IActionDefinition,
	IStateProviderActionDefinition
{
	private const string ModeParameter = "mode";
	private const string ModeToggle = "toggle";
	private const string ModeOn = "on";
	private const string ModeOff = "off";

	private const string StateOn = "on";
	private const string StateOff = "off";

	private static readonly IReadOnlyList<ActionStateDefinition> States =
	[
		new(StateOn, Strings.Actions.MidiListening.StateOn())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2E7D32" },
		},
		new(StateOff, Strings.Actions.MidiListening.StateOff()),
	];

	public string Id => "midi-listening";

	public LocalizedText Name => Strings.Actions.MidiListening.Name();

	public LocalizedText Description => Strings.Actions.MidiListening.Description();

	public IReadOnlyList<ActionParameter> Parameters { get; } =
	[
		ActionParameter.Choice(
			ModeParameter,
			options:
			[
				new ActionParameterOption { Value = ModeToggle, Label = Strings.Actions.MidiListening.Mode.Toggle() },
				new ActionParameterOption { Value = ModeOn, Label = Strings.Actions.MidiListening.Mode.On() },
				new ActionParameterOption { Value = ModeOff, Label = Strings.Actions.MidiListening.Mode.Off() },
			],
			label: Strings.Actions.MidiListening.Mode.Label(),
			description: Strings.Actions.MidiListening.Mode.Description(),
			defaultValue: ModeToggle),
	];

	public MacroDeckPlatform Platforms => MacroDeckPlatform.Windows | MacroDeckPlatform.MacOs;

	// Picks up a change made by another button bound to this action.
	public TimeSpan StatePollInterval => TimeSpan.FromSeconds(2);

	public Task<ActionStateSnapshot?> GetActionStateAsync(
		IReadOnlyDictionary<string, object?> parameters,
		CancellationToken cancellationToken) =>
		Task.FromResult<ActionStateSnapshot?>(new(States, listener.Enabled ? StateOn : StateOff));

	public IActionExecutor CreateExecutor() => new Executor(this);

	private async Task<bool> SetModeAsync(string? mode)
	{
		var enabled = mode switch
		{
			ModeOn => true,
			ModeOff => false,
			_ => !listener.Enabled,
		};

		listener.Enabled = enabled;
		await saveEnabled(enabled);
		return enabled;
	}

	private sealed class Executor(MidiListeningAction action) : IActionExecutor
	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			var mode = context.Parameters.TryGetValue(ModeParameter, out var value)
				? value?.ToString()
				: null;

			var enabled = await action.SetModeAsync(mode);
			return ActionResult.Success(enabled ? StateOn : StateOff);
		}
	}
}
