using System.Globalization;
using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Events;
using Serilog;

namespace DoriDeck.MidiTrigger;

// Publishes a "MIDI key" event so Macro Deck button or automation can be attached to a key on a MIDI
public sealed class MidiTriggerIntegration
	: IPluginIntegration,
		IEventProvider,
		IDynamicEventOptionsProvider,
		IDisposable
{
	internal const string NotePressedEventId = "midi-note-pressed";
	internal const string DeviceParameterName = "deviceId";
	internal const string NoteParameterName = "note";

	private static readonly string[] PitchClassNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

	private const string ListeningStorageKey = "midi-listening";

	private readonly ILogger _logger;
	private readonly MidiInputListener _listener;

	private IIntegrationContext? _context;
	private IReadOnlySet<string> _boundDevices = new HashSet<string>(StringComparer.Ordinal);
	// Bound notes per device, so unbound keys never reach the host. Null means "publish every note" for a binding
	// whose note filter this plugin cannot read (e.g. an operator other than ==).
	private IReadOnlyDictionary<string, HashSet<int>?> _boundNotes = new Dictionary<string, HashSet<int>?>(StringComparer.Ordinal);

	public MidiTriggerIntegration(ILogger logger)
	{
		_logger = logger.ForContext<MidiTriggerIntegration>();
		_listener = new MidiInputListener(logger);
		_listener.NotePressed += OnNotePressed;
		Actions = [new MidiListeningAction(_listener, SaveListeningAsync)];
	}

	public string ProviderName => "DoriDeck MIDI";

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<EventDefinition> EventDefinitions { get; } =
	[
		new EventDefinition
		{
			Id = NotePressedEventId,
			Name = Strings.Events.MidiNotePressed.Name(),
			Description = Strings.Events.MidiNotePressed.Description(),
			ConfigurationParameters =
			[
				ActionParameter.DynamicChoice(
					DeviceParameterName,
					label: Strings.Events.MidiNotePressed.Device.Label(),
					description: Strings.Events.MidiNotePressed.Device.Description(),
					placeholder: Strings.Events.MidiNotePressed.Device.Placeholder(),
					required: true),
				ActionParameter.Choice(
					NoteParameterName,
					options: BuildNoteOptions(),
					label: Strings.Events.MidiNotePressed.Note.Label(),
					description: Strings.Events.MidiNotePressed.Note.Description(),
					required: true),
			],
			PayloadParameters =
			[
				ActionParameter.DynamicChoice(
					DeviceParameterName,
					label: Strings.Events.MidiNotePressed.Device.Label()),
				ActionParameter.Choice(
					NoteParameterName,
					options: BuildNoteOptions(),
					label: Strings.Events.MidiNotePressed.Note.Label()),
			],
		},
	];

	public async Task InitializeAsync(IIntegrationContext context)
	{
		_context = context;
		context.Events.BindingsChanged -= ApplyBindings;
		context.Events.BindingsChanged += ApplyBindings;

		// Restore the on/off switch before the first poll, so a released keyboard is not grabbed at startup.
		var listening = await context.Storage.ReadAsync(ListeningStorageKey);
		_listener.Enabled = !bool.TryParse(listening.Content, out var enabled) || enabled;

		ApplyBindings();
		_listener.Start();
		_logger.Information("Initialized. MIDI listening is {State}.", _listener.Enabled ? "on" : "off");
	}

	private Task SaveListeningAsync(bool enabled) =>
		_context?.Storage.WriteAsync(ListeningStorageKey, enabled.ToString(CultureInfo.InvariantCulture)) ?? Task.CompletedTask;

	public Task ShutdownAsync()
	{
		if (_context is { } context)
		{
			context.Events.BindingsChanged -= ApplyBindings;
		}

		_listener.Dispose();
		_context = null;
		return Task.CompletedTask;
	}

	public void Dispose() => _listener.Dispose();

	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context, CancellationToken cancellationToken)
	{
		if (context.ParameterName != DeviceParameterName)
		{
			return Task.FromResult(new DynamicOptionsResult { Options = [] });
		}

		// Keep devices that are bound but currently unplugged in the list, so existing bindings still show their value.
		var names = MidiInputListener.GetDeviceNames()
			.Concat(_boundDevices)
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal);

		IReadOnlyList<ActionParameterOption> options =
		[
			.. names.Select(name => new ActionParameterOption { Value = name, Label = LocalizedText.FromLiteral(name) }),
		];

		return Task.FromResult(new DynamicOptionsResult { Options = options });
	}

	private void ApplyBindings()
	{
		if (_context is not { } context)
		{
			return;
		}

		var notes = new Dictionary<string, HashSet<int>?>(StringComparer.Ordinal);
		foreach (var binding in context.Events.GetBindings())
		{
			if (string.Equals(binding.EventId, NotePressedEventId, StringComparison.Ordinal)
				&& binding.Parameters.TryGetValue(DeviceParameterName, out var value)
				&& value is { Operator: "==", Value: { ValueKind: JsonValueKind.String } device }
				&& device.GetString() is { Length: > 0 } deviceName)
			{
				var known = notes.TryGetValue(deviceName, out var existing) ? existing : [];
				if (known is not null && TryReadNote(binding, out var note))
				{
					known.Add(note);
					notes[deviceName] = known;
				}
				else
				{
					notes[deviceName] = null;
				}
			}
		}

		_boundNotes = notes;
		_boundDevices = new HashSet<string>(notes.Keys, StringComparer.Ordinal);
		_listener.SetDevices(_boundDevices);
	}

	private void OnNotePressed(string deviceName, int note)
	{
		if (_context is not { } context
			|| !_boundNotes.TryGetValue(deviceName, out var notes)
			|| (notes is not null && !notes.Contains(note)))
		{
			return;
		}

		context.Events.Publish(NotePressedEventId, new Dictionary<string, object?>
		{
			[DeviceParameterName] = deviceName,
			[NoteParameterName] = note.ToString(CultureInfo.InvariantCulture),
		});
	}

	private static bool TryReadNote(EventBinding binding, out int note)
	{
		note = 0;
		return binding.Parameters.TryGetValue(NoteParameterName, out var value)
			&& value is { Operator: "==", Value: { ValueKind: JsonValueKind.String } text }
			&& int.TryParse(text.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out note);
	}

	// Middle C (60) is C4, matching the piano folder's note names.
	private static IReadOnlyList<ActionParameterOption> BuildNoteOptions() =>
	[
		.. Enumerable.Range(0, 128).Select(note => new ActionParameterOption
		{
			Value = note.ToString(CultureInfo.InvariantCulture),
			Label = LocalizedText.FromLiteral(
				string.Create(CultureInfo.InvariantCulture, $"{PitchClassNames[note % 12]}{note / 12 - 1} ({note})")),
		}),
	];
}
