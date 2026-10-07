using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScoreInterface.Json;

/// <summary>
/// Reads a boolean that Dorico may send either as a JSON boolean or as a string ("true", "false", "1", "0"),
/// as it already does for some numeric status fields. Anything unrecognized reads as <c>false</c>.
/// </summary>
internal sealed class FlexibleBooleanConverter : JsonConverter<bool>
{
	public override bool Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
		reader.TokenType switch
		{
			JsonTokenType.True => true,
			JsonTokenType.False => false,
			JsonTokenType.Number => reader.TryGetInt64(out var number) && number != 0,
			JsonTokenType.String => reader.GetString() is { } text &&
				(bool.TryParse(text, out var parsed) ? parsed : text == "1"),
			_ => false,
		};

	public override void Write(Utf8JsonWriter writer, bool value, JsonSerializerOptions options) =>
		writer.WriteBooleanValue(value);
}
