using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoriDeck.MidiBridge;

public sealed record SubscriptionDto(
	string Type,
	IReadOnlyList<int>? Numbers = null,
	IReadOnlyList<string>? Modes = null);

public sealed record SubscribeRequest(
	string Topic,
	string Key,
	int LeaseSeconds,
	IReadOnlyList<SubscriptionDto> Subscriptions);

public sealed record SubscribeResponse(int Rules, int LeaseSeconds, string? Error, RouterState? State);

public sealed record RouterState(
	string? Mode = null,
	string? PendingMode = null,
	bool Pending = false,
	IReadOnlyList<string>? Modes = null,
	bool Enabled = true,
	string? Status = null,
	IReadOnlyList<string>? Devices = null);

public sealed record DeliveredMessage(
	string? Device,
	string? Type,
	int Channel,
	int Number,
	int Value,
	string? Mode = null);

public static class RouterProtocol
{
	public const string SubscribeTopic = "midi-router.subscribe";
	public const string StateTopic = "midi-router.state";
	public const string StateChangedTopic = "midi-router.state-changed";

	public const string DeliveryTopic = "dorideck.midi";

	public const string SubscriptionKey = "midi-bridge";

	public const int LeaseSeconds = 360;

	public const string NoteType = "note";
	public const string NoteOnType = "note-on";

	public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public static SubscribeRequest BuildSubscribeRequest(IEnumerable<MidiBinding> bindings, string mode)
	{
		var notes = bindings
			.Select(binding => binding.Note)
			.Distinct()
			.Order()
			.ToList();

		IReadOnlyList<SubscriptionDto> subscriptions = notes.Count == 0
			? []
			: [new SubscriptionDto(NoteType, notes, [mode])];

		return new SubscribeRequest(DeliveryTopic, SubscriptionKey, LeaseSeconds, subscriptions);
	}

	public static MidiBinding? FindTriggeredBinding(IReadOnlyDictionary<int, MidiBinding> bindings, DeliveredMessage? message)
	{
		if (message is null ||
			!string.Equals(message.Type, NoteOnType, StringComparison.OrdinalIgnoreCase) ||
			message.Value <= 0)
		{
			return null;
		}

		return bindings.GetValueOrDefault(message.Number);
	}
}
