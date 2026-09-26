using System.Buffers.Binary;
using Rusty.Engine;

namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>
/// Parses the SHA-256 a publication states for an artifact into the Engine's own digest value.
/// </summary>
/// <remarks>
/// Two readers join published digests against descriptors — the classic-media inventory and the music
/// manifest — and both must answer the same question the same way: whether the bytes a publication
/// measured are the bytes a descriptor names. One parser keeps that comparison from being two dialects.
/// </remarks>
internal static class DaggerfallContentHash
{
    /// <summary>Parses one lowercase hexadecimal digest, naming the publisher when it is malformed.</summary>
    internal static ContentSha256 Parse(string value, string publisher)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        ArgumentException.ThrowIfNullOrWhiteSpace(publisher);
        try
        {
            byte[] bytes = Convert.FromHexString(value);
            if (bytes.Length != 32) throw new FormatException("SHA-256 needs 32 bytes.");
            return new ContentSha256(
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(0, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(8, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(16, 8)),
                BinaryPrimitives.ReadUInt64BigEndian(bytes.AsSpan(24, 8)));
        }
        catch (FormatException)
        {
            throw new InvalidOperationException($"{publisher} has an invalid SHA-256.");
        }
    }
}
