using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DoriDeck.MidiBridge;


public sealed class MidiBinding
{
	/// MIDI note number, 0-127 (60 = C4).
	public int Note { get; init; }

	public string ActionId { get; init; } = string.Empty;

	public Dictionary<string, object?> Parameters { get; init; } = new(StringComparer.Ordinal);

	public string? Icon { get; init; }
}

/// MIDI bridge keeps data in midi-bridge.json
public sealed record MidiBridgeDocument
{
	public const int CurrentVersion = 1;

	public const string DefaultMode = "Command";

	public int Version { get; init; } = CurrentVersion;

	public string Mode { get; init; } = DefaultMode;

	public IReadOnlyList<MidiBinding> Bindings { get; init; } = [];
}

public static class MidiBridgeSerializer
{
	public const int MinNote = 0;
	public const int MaxNote = 127;

	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	public static string Serialize(MidiBridgeDocument document) => JsonSerializer.Serialize(document, Options);

	public static MidiBridgeDocument Deserialize(string json)
	{
		var document = JsonSerializer.Deserialize<MidiBridgeDocument>(json, Options) ?? new MidiBridgeDocument();

		var bindings = new Dictionary<int, MidiBinding>();
		foreach (var binding in document.Bindings ?? [])
		{
			if (binding is null || binding.Note is < MinNote or > MaxNote || string.IsNullOrWhiteSpace(binding.ActionId))
			{
				continue;
			}

			bindings[binding.Note] = new MidiBinding
			{
				Note = binding.Note,
				ActionId = binding.ActionId,
				Icon = string.IsNullOrWhiteSpace(binding.Icon) ? null : binding.Icon,
				Parameters = (binding.Parameters ?? [])
					.ToDictionary(pair => pair.Key, pair => Normalize(pair.Value), StringComparer.Ordinal),
			};
		}

		return document with
		{
			Mode = string.IsNullOrWhiteSpace(document.Mode) ? MidiBridgeDocument.DefaultMode : document.Mode,
			Bindings = [.. bindings.Values.OrderBy(binding => binding.Note)],
		};
	}

	private static object? Normalize(object? value) => value switch
	{
		JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
		JsonElement { ValueKind: JsonValueKind.True } => true,
		JsonElement { ValueKind: JsonValueKind.False } => false,
		JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
		JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
		JsonElement element => element.GetRawText(),
		_ => value,
	};

	// I know, too much of copy-paste, but easy too modify and test
	public static string NoteName(int note)
	{
		string[] names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
		return names[note % 12] + (note / 12 - 1).ToString(CultureInfo.InvariantCulture);
	}
}
