using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScoreInterface.Enums;

public enum WindowMode
{
    Undefined = 0,
    kSetupMode,
    kWriteMode,
    kEngraveMode,
    kPlayMode,
    kPrintMode
}

public enum NoteInputMode
{
    Undefined = 0,
    kInsert,
    kOverwrite,
    kChordMerge
}

public enum Accidental
{
    None = 0,
    kNatural,
    kSharp,
    kDoubleSharp,
    kTripleSharp,
    kFlat,
    kDoubleFlat,
    kTripleFlat
}

/// <summary>
/// Produces enum converters that never throw on an unrecognized value. A newer Dorico version can
/// send a string or number for one of these enums that this build doesn't know about yet; rather
/// than failing the whole message (and losing every other field in it), the unknown value
/// deserializes to the enum's default member.
/// </summary>
internal sealed class SafeEnumConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert) =>
        (Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert).IsEnum;

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var enumType = Nullable.GetUnderlyingType(typeToConvert) ?? typeToConvert;
        var converter = (JsonConverter)Activator.CreateInstance(
            typeof(SafeEnumConverter<>).MakeGenericType(enumType))!;

        if (Nullable.GetUnderlyingType(typeToConvert) is not null)
        {
            return (JsonConverter)Activator.CreateInstance(
                typeof(NullableSafeEnumConverter<>).MakeGenericType(enumType), converter)!;
        }

        return converter;
    }

    private sealed class SafeEnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                return !string.IsNullOrWhiteSpace(value) && Enum.TryParse<TEnum>(value, true, out var parsed)
                    ? parsed
                    : default;
            }

            if (reader.TokenType == JsonTokenType.Number &&
                reader.TryGetInt32(out var number) &&
                Enum.IsDefined(typeof(TEnum), number))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            }

            if (reader.TokenType != JsonTokenType.Null)
            {
                try { reader.Skip(); } catch (JsonException) { }
            }

            return default;
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.ToString());
    }

    private sealed class NullableSafeEnumConverter<TEnum>(JsonConverter<TEnum> inner) : JsonConverter<TEnum?>
        where TEnum : struct, Enum
    {
        public override TEnum? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.Null ? null : inner.Read(ref reader, typeof(TEnum), options);

        public override void Write(Utf8JsonWriter writer, TEnum? value, JsonSerializerOptions options)
        {
            if (value is null)
            {
                writer.WriteNullValue();
            }
            else
            {
                inner.Write(writer, value.Value, options);
            }
        }
    }
}
