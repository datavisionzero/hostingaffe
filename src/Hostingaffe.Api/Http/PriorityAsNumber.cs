using System.Text.Json;
using System.Text.Json.Serialization;
using Hostingaffe.Domain.Issues;

namespace Hostingaffe.Api.Http;

/// <summary>
/// Priority is the one enum that travels as its number (VISION 8): <c>0</c> to
/// <c>4</c>, monotonically increasing, so that a client sorts and compares
/// without a table. Registered before the string converter, which would
/// otherwise spell it.
/// </summary>
public sealed class PriorityAsNumber : JsonConverter<Priority>
{
    public override Priority Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        reader.TokenType is JsonTokenType.Number && reader.TryGetInt32(out var number) && number is >= 0 and <= 4
            ? (Priority)number
            : throw new JsonException("Priority is a number from 0 to 4.");

    public override void Write(Utf8JsonWriter writer, Priority value, JsonSerializerOptions options) =>
        writer.WriteNumberValue((int)value);
}
