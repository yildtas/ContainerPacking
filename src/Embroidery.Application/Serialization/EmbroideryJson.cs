using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Embroidery.Core.Primitives;

namespace Embroidery.Application.Serialization;

/// <summary>Shared JSON contract for project files, the HTTP API and content hashing.</summary>
public static class EmbroideryJson
{
    public static JsonSerializerOptions Options { get; } = Create(indented: false);
    public static JsonSerializerOptions Indented { get; } = Create(indented: true);

    private static JsonSerializerOptions Create(bool indented)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = indented,
            NumberHandling = JsonNumberHandling.Strict,
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        options.Converters.Add(new Vec2Converter());
        return options;
    }

    /// <summary>
    /// Stable SHA-256 of a value's canonical JSON. Property order follows declaration order
    /// and numbers are formatted invariantly, so the hash is identical across processes.
    /// (Never use GetHashCode for persisted keys: it is randomised per process.)
    /// </summary>
    public static string Hash<T>(T value)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    public static string Hash(params object?[] parts)
    {
        var sb = new StringBuilder();
        foreach (var part in parts) sb.Append(JsonSerializer.Serialize(part, part?.GetType() ?? typeof(object), Options)).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }
}

/// <summary>Writes points compactly as [x, y].</summary>
public sealed class Vec2Converter : JsonConverter<Vec2>
{
    public override Vec2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("Expected [x, y].");
        reader.Read();
        var x = reader.GetDouble();
        reader.Read();
        var y = reader.GetDouble();
        reader.Read();
        if (reader.TokenType != JsonTokenType.EndArray) throw new JsonException("Expected [x, y].");
        if (!double.IsFinite(x) || !double.IsFinite(y)) throw new JsonException("Coordinates must be finite.");
        return new Vec2(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Vec2 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(Math.Round(value.X, 5));
        writer.WriteNumberValue(Math.Round(value.Y, 5));
        writer.WriteEndArray();
    }
}
