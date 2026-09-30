using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WorldRpg.SpriteAuthoring;

/// <summary>Canonical lowercase SHA-256 content address carried by sprite inspection and overlay documents.</summary>
[JsonConverter(typeof(SpriteContentDigestJsonConverter))]
public readonly record struct SpriteContentDigest
{
    public SpriteContentDigest(string value)
    {
        if (!IsCanonical(value))
        {
            throw new ArgumentException("A content digest must be a lowercase 64-character SHA-256 hexadecimal value.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static SpriteContentDigest Compute(ReadOnlySpan<byte> bytes) => new(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());

    public override string ToString() => Value;

    public void Validate()
    {
        if (!IsCanonical(Value))
        {
            throw new InvalidOperationException("A content digest must be a lowercase 64-character SHA-256 hexadecimal value.");
        }
    }

    private static bool IsCanonical(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
}

public sealed class SpriteContentDigestJsonConverter : JsonConverter<SpriteContentDigest>
{
    public override SpriteContentDigest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        new(reader.GetString() ?? throw new JsonException("A content digest cannot be null."));

    public override void Write(Utf8JsonWriter writer, SpriteContentDigest value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value);
}

/// <summary>A two-component authored or source presentation value.</summary>
public readonly record struct SpriteVector2(float X, float Y)
{
    public void Validate(string name)
    {
        if (!float.IsFinite(X) || !float.IsFinite(Y))
        {
            throw new ArgumentOutOfRangeException(name, "A sprite vector value must be finite.");
        }
    }
}

/// <summary>The logical identity and relative-path rules every sprite-authoring document obeys.</summary>
public static class SpriteLogicalNames
{
    public static void RequireId(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsWhiteSpace))
        {
            throw new ArgumentException("A logical ID must be non-empty and whitespace-free.", name);
        }
    }

    public static void RequirePath(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.StartsWith('/')
            || value.Contains('\\', StringComparison.Ordinal)
            || value.Split('/').Any(segment => segment is "." or ".." or ""))
        {
            throw new ArgumentException("A logical path must be relative, slash-separated, and cannot contain dot segments.", name);
        }
    }
}
