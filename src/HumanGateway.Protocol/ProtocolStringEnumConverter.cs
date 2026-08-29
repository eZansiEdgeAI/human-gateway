using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HumanGateway.Protocol;

/// <summary>
/// Serializes and deserializes protocol enums using their exact wire strings.
/// </summary>
/// <remarks>
/// The protocol schemas use UPPER_SNAKE and lowercase wire values ("WAITING_FOR_SYNC",
/// "human", "PUSH", ...) which are not valid C# identifiers. Each enum member carries the
/// wire value via <see cref="EnumMemberAttribute"/>; this converter is the single mapping
/// point for both directions, so a deserialized value always re-serializes to the identical
/// token (round-trip fidelity, protocol §6). Unknown tokens throw <see cref="JsonException"/>,
/// mirroring JSON Schema enum rejection.
/// </remarks>
public sealed class ProtocolStringEnumConverter : JsonConverterFactory
{
    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;

    /// <inheritdoc />
    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        var converterType = typeof(StringEnumConverter<>).MakeGenericType(typeToConvert);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }

    private sealed class StringEnumConverter<T> : JsonConverter<T>
        where T : struct, Enum
    {
        private static readonly Dictionary<string, T> FromWire = new(StringComparer.Ordinal);
        private static readonly Dictionary<T, string> ToWire = new();

        static StringEnumConverter()
        {
            foreach (var name in Enum.GetNames<T>())
            {
                var wire = typeof(T).GetField(name)?.GetCustomAttribute<EnumMemberAttribute>()?.Value ?? name;
                var value = Enum.Parse<T>(name);
                FromWire[wire] = value;
                ToWire[value] = wire;
            }
        }

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var token = reader.GetString();
            if (token is not null && FromWire.TryGetValue(token, out var value))
            {
                return value;
            }
            throw new JsonException(
                $"'{token}' is not a valid {typeof(T).Name} value (protocol enum wire values are fixed by the v1 schemas).");
        }

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(ToWire.TryGetValue(value, out var wire) ? wire : value.ToString());
        }
    }
}
