using ScoreInterface.Enums;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScoreInterface.Json;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance used to (de)serialize every message
/// exchanged with Dorico. Property names are matched case-insensitively and enum values that
/// don't map to a known member deserialize to the enum's default value instead of throwing, so a
/// newer Dorico version that adds fields or enum members we don't know about yet cannot break
/// deserialization of the fields we do know about.
/// </summary>
internal static class ScoreInterfaceJsonOptions
{
    public static readonly JsonSerializerOptions Options = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        options.Converters.Add(new SafeEnumConverterFactory());

        return options;
    }
}
