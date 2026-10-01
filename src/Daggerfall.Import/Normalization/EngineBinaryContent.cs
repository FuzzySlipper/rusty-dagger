using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Normalization;

/// <summary>One collision position of a binary Spatial content artifact.</summary>
public readonly record struct SpatialArtifactPosition(double X, double Y, double Z);

/// <summary>One collision triangle of a binary Spatial content artifact, as indices into its positions.</summary>
public readonly record struct SpatialArtifactTriangle(uint A, uint B, uint C);

/// <summary>
/// One navigation cell of a binary Spatial content artifact. The encoding does not store the level: the
/// Engine derives it as <c>round(supportHeight / levelQuantum)</c>, so the writer refuses a cell whose
/// stated level differs.
/// </summary>
public readonly record struct SpatialArtifactCell(int Column, int Row, long Level, double SupportHeight, bool Walkable);

/// <summary>The navigation derivation values a Spatial content artifact states.</summary>
public sealed record SpatialArtifactNavigationConfig(
    double CellSize,
    double LevelQuantum,
    double MaximumSlopeDegrees,
    double RequiredHeadroom,
    double SupportProbeDrop);

/// <summary>The facts of an Engine Spatial content artifact: the same facts its JSON form states.</summary>
public sealed record SpatialArtifactFacts(
    string StaticMeshArtifactId,
    SpatialArtifactPosition BoundsMinimum,
    SpatialArtifactPosition BoundsMaximum,
    IReadOnlyList<SpatialArtifactPosition> Positions,
    IReadOnlyList<SpatialArtifactTriangle> Triangles,
    string NavigationId,
    SpatialArtifactNavigationConfig NavigationConfig,
    IReadOnlyList<SpatialArtifactCell> Cells);

/// <summary>
/// Writes the Engine's binary Spatial content artifact (<c>RSPATIAL</c>), which
/// <c>Spatial.ReplaceContentArtifact</c> admits with the JSON form's validation and refusals. The layout
/// is the Engine's documented one (rusty-engine <c>docs/csharp-lifecycle.md</c>); there is no SDK writer.
/// Little-endian, no padding: magic, the artifact and navigation-config schema versions (u32 each),
/// bounds (6 f64), the five navigation config values (f64), the static mesh artifact id and navigation id
/// (u32 byte length + UTF-8 each), the position, triangle and cell counts (u64 each), positions (3 f64
/// each), triangles (3 u32 each), every cell column then every cell row (i32), cell support heights (f64)
/// and a walkable bitset (cell <c>i</c> at bit <c>i % 8</c> of byte <c>i / 8</c>).
/// </summary>
public static class SpatialArtifactBinary
{
    /// <summary>The recommended extension, the Engine fixtures' own.</summary>
    public const string Extension = ".rspatial";

    private static ReadOnlySpan<byte> Magic => "RSPATIAL"u8;

    /// <summary>The format version the Engine's artifact states, and its navigation config.</summary>
    private const uint SchemaVersion = 1;

    public static byte[] Write(SpatialArtifactFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        SpatialArtifactNavigationConfig config = facts.NavigationConfig;
        byte[] staticMeshId = Encoding.UTF8.GetBytes(facts.StaticMeshArtifactId);
        byte[] navigationId = Encoding.UTF8.GetBytes(facts.NavigationId);
        int cells = facts.Cells.Count;
        long length = Magic.Length + (2 * sizeof(uint)) + (11 * sizeof(double))
            + sizeof(uint) + staticMeshId.Length + sizeof(uint) + navigationId.Length
            + (3 * sizeof(ulong))
            + (facts.Positions.Count * 3L * sizeof(double))
            + (facts.Triangles.Count * 3L * sizeof(uint))
            + (cells * ((2L * sizeof(int)) + sizeof(double)))
            + ((cells + 7L) / 8);
        byte[] bytes = new byte[checked((int)length)];
        Span<byte> output = bytes;
        int at = 0;
        Magic.CopyTo(output);
        at += Magic.Length;
        WriteUInt32(output, ref at, SchemaVersion);
        WriteUInt32(output, ref at, SchemaVersion);
        WritePosition(output, ref at, facts.BoundsMinimum);
        WritePosition(output, ref at, facts.BoundsMaximum);
        WriteDouble(output, ref at, config.CellSize);
        WriteDouble(output, ref at, config.LevelQuantum);
        WriteDouble(output, ref at, config.MaximumSlopeDegrees);
        WriteDouble(output, ref at, config.RequiredHeadroom);
        WriteDouble(output, ref at, config.SupportProbeDrop);
        WriteText(output, ref at, staticMeshId);
        WriteText(output, ref at, navigationId);
        WriteUInt64(output, ref at, (ulong)facts.Positions.Count);
        WriteUInt64(output, ref at, (ulong)facts.Triangles.Count);
        WriteUInt64(output, ref at, (ulong)cells);
        foreach (SpatialArtifactPosition position in facts.Positions) WritePosition(output, ref at, position);
        foreach (SpatialArtifactTriangle triangle in facts.Triangles)
        {
            WriteUInt32(output, ref at, triangle.A);
            WriteUInt32(output, ref at, triangle.B);
            WriteUInt32(output, ref at, triangle.C);
        }
        foreach (SpatialArtifactCell cell in facts.Cells) WriteInt32(output, ref at, cell.Column);
        foreach (SpatialArtifactCell cell in facts.Cells) WriteInt32(output, ref at, cell.Row);
        foreach (SpatialArtifactCell cell in facts.Cells)
        {
            // The Engine derives the level from the height; a different stated level would be silently replaced.
            if (Math.Round(cell.SupportHeight / config.LevelQuantum, MidpointRounding.AwayFromZero) != cell.Level)
            {
                throw new InvalidOperationException(
                    $"Navigation cell ({cell.Column}, {cell.Row}) states level {cell.Level}, but the Engine derives a different level from support height {cell.SupportHeight.ToString(CultureInfo.InvariantCulture)}.");
            }
            WriteDouble(output, ref at, cell.SupportHeight);
        }
        for (int index = 0; index < cells; index++)
        {
            if (facts.Cells[index].Walkable) output[at + (index / 8)] |= (byte)(1 << (index % 8));
        }
        at += (cells + 7) / 8;
        if (at != bytes.Length) throw new InvalidOperationException("The binary Spatial artifact length was miscounted.");
        return bytes;
    }

    private static void WritePosition(Span<byte> output, ref int at, SpatialArtifactPosition position)
    {
        WriteDouble(output, ref at, position.X);
        WriteDouble(output, ref at, position.Y);
        WriteDouble(output, ref at, position.Z);
    }

    private static void WriteText(Span<byte> output, ref int at, byte[] text)
    {
        WriteUInt32(output, ref at, checked((uint)text.Length));
        text.CopyTo(output[at..]);
        at += text.Length;
    }

    private static void WriteDouble(Span<byte> output, ref int at, double value)
    {
        BinaryPrimitives.WriteDoubleLittleEndian(output[at..], value);
        at += sizeof(double);
    }

    private static void WriteUInt32(Span<byte> output, ref int at, uint value)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(output[at..], value);
        at += sizeof(uint);
    }

    private static void WriteInt32(Span<byte> output, ref int at, int value)
    {
        BinaryPrimitives.WriteInt32LittleEndian(output[at..], value);
        at += sizeof(int);
    }

    private static void WriteUInt64(Span<byte> output, ref int at, ulong value)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(output[at..], value);
        at += sizeof(ulong);
    }
}

/// <summary>One index range of a static mesh drawn with one material slot.</summary>
public readonly record struct StaticMeshGroup(int MaterialSlot, int Start, int Count);

/// <summary>One static mesh material slot binding.</summary>
public readonly record struct StaticMeshMaterialBinding(string Material, int Slot);

/// <summary>
/// The facts of an Engine static mesh: f32 positions and normals, optional UVs, u32 indices, the groups
/// drawing them, the bounds and the material slots. Bounds are the JSON form's; the mesh is visual only.
/// </summary>
public sealed record StaticMeshFacts(
    string Asset,
    NormalizedBounds Bounds,
    IReadOnlyList<NormalizedVector3> Positions,
    IReadOnlyList<NormalizedVector3> Normals,
    IReadOnlyList<NormalizedVector2>? Uvs,
    IReadOnlyList<uint> Indices,
    IReadOnlyList<StaticMeshGroup> Groups,
    IReadOnlyList<StaticMeshMaterialBinding> MaterialSlots);

/// <summary>
/// Writes the Engine's binary static mesh (<c>RSTATMSH</c>), which
/// <c>Graphics.CreateStaticMeshFromContent</c> admits with the JSON form's validation and refusals
/// (rusty-engine <c>docs/csharp-product-project.md</c>, "Binary static meshes"). Little-endian: the
/// magic, a u32 descriptor length, the descriptor (the JSON form's static mesh document whose payload
/// source is a <c>resource</c> naming the bytes that follow), then the Engine's packed mesh resource: an
/// 8-byte magic (<c>RMSHLE01</c> positions and normals, <c>RMSHLE02</c> with UVs), its total byte length
/// and payload count (1) as u32, then f32 positions, normals and UVs and u32 indices. The resource's
/// identity is <c>mesh-resource/&lt;sha256 hex&gt;</c> of those bytes, the content hash the Engine checks.
/// </summary>
public static class StaticMeshBinary
{
    /// <summary>The recommended extension, the Engine fixtures' own.</summary>
    public const string Extension = ".rstatmsh";

    private static ReadOnlySpan<byte> Magic => "RSTATMSH"u8;

    private const int ResourceHeaderBytes = 16;

    public static byte[] Write(StaticMeshFacts facts)
    {
        ArgumentNullException.ThrowIfNull(facts);
        bool uvs = facts.Uvs is not null;
        int vertices = facts.Positions.Count;
        if (facts.Normals.Count != vertices || (uvs && facts.Uvs!.Count != vertices))
            throw new ArgumentException("Static mesh streams must carry one normal (and UV) per position.", nameof(facts));

        // The packed mesh resource: header, then each stream in the encoding's fixed order.
        long resourceLength = ResourceHeaderBytes
            + (vertices * (uvs ? 8L : 6L) * sizeof(float))
            + (facts.Indices.Count * (long)sizeof(uint));
        byte[] resource = new byte[checked((int)resourceLength)];
        Span<byte> output = resource;
        (uvs ? "RMSHLE02"u8 : "RMSHLE01"u8).CopyTo(output);
        BinaryPrimitives.WriteUInt32LittleEndian(output[8..], (uint)resource.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(output[12..], 1);
        int at = ResourceHeaderBytes;
        int positionsOffset = at;
        foreach (NormalizedVector3 value in facts.Positions) WriteVector3(output, ref at, value);
        int normalsOffset = at;
        foreach (NormalizedVector3 value in facts.Normals) WriteVector3(output, ref at, value);
        int uvsOffset = at;
        if (uvs)
        {
            foreach (NormalizedVector2 value in facts.Uvs!)
            {
                WriteSingle(output, ref at, value.X);
                WriteSingle(output, ref at, value.Y);
            }
        }
        int indicesOffset = at;
        foreach (uint index in facts.Indices)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(output[at..], index);
            at += sizeof(uint);
        }
        if (at != resource.Length) throw new InvalidOperationException("The packed mesh resource length was miscounted.");
        string digest = Convert.ToHexStringLower(SHA256.HashData(resource));

        byte[] descriptor = Descriptor(facts, digest, resource.Length, positionsOffset, normalsOffset, uvs ? uvsOffset : null, indicesOffset);
        byte[] bytes = new byte[checked(Magic.Length + sizeof(uint) + descriptor.Length + resource.Length)];
        Magic.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(Magic.Length), (uint)descriptor.Length);
        descriptor.CopyTo(bytes, Magic.Length + sizeof(uint));
        resource.CopyTo(bytes, Magic.Length + sizeof(uint) + descriptor.Length);
        return bytes;
    }

    private static byte[] Descriptor(StaticMeshFacts facts, string digest, int byteLength, int positionsOffset, int normalsOffset, int? uvsOffset, int indicesOffset)
    {
        using MemoryStream stream = new();
        using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("asset", facts.Asset);
            writer.WritePropertyName("payload");
            writer.WriteStartObject();
            writer.WritePropertyName("layout");
            writer.WriteStartObject();
            writer.WriteNumber("vertexCount", facts.Positions.Count);
            writer.WriteNumber("indexCount", facts.Indices.Count);
            writer.WriteString("indexWidth", "u32");
            writer.WritePropertyName("attributes");
            writer.WriteStartArray();
            WriteAttribute(writer, "position", 3);
            WriteAttribute(writer, "normal", 3);
            if (uvsOffset is not null) WriteAttribute(writer, "uv", 2);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WritePropertyName("groups");
            writer.WriteStartArray();
            foreach (StaticMeshGroup group in facts.Groups)
            {
                writer.WriteStartObject();
                writer.WriteNumber("materialSlot", group.MaterialSlot);
                writer.WriteNumber("start", group.Start);
                writer.WriteNumber("count", group.Count);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("bounds");
            writer.WriteStartObject();
            WriteVector3(writer, "min", facts.Bounds.Minimum);
            WriteVector3(writer, "max", facts.Bounds.Maximum);
            writer.WriteEndObject();
            writer.WritePropertyName("source");
            writer.WriteStartObject();
            writer.WriteString("kind", "resource");
            writer.WriteString("resource", $"mesh-resource/{digest}");
            writer.WriteString("contentHash", $"sha256:{digest}");
            writer.WriteNumber("byteLength", byteLength);
            writer.WriteString("encoding", uvsOffset is null ? "packedStreamsLeV1" : "packedStreamsLeV2");
            writer.WriteNumber("positionsByteOffset", positionsOffset);
            writer.WriteNumber("normalsByteOffset", normalsOffset);
            if (uvsOffset is int uv) writer.WriteNumber("uvsByteOffset", uv);
            writer.WriteNumber("indicesByteOffset", indicesOffset);
            writer.WriteEndObject();
            writer.WriteString("provenance", "staticAsset");
            writer.WriteEndObject();
            writer.WritePropertyName("materialSlots");
            writer.WriteStartArray();
            foreach (StaticMeshMaterialBinding binding in facts.MaterialSlots)
            {
                writer.WriteStartObject();
                writer.WriteNumber("slot", binding.Slot);
                writer.WriteString("material", binding.Material);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WritePropertyName("collision");
            writer.WriteStartObject();
            writer.WriteString("kind", "visualOnly");
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteAttribute(Utf8JsonWriter writer, string name, int components)
    {
        writer.WriteStartObject();
        writer.WriteString("name", name);
        writer.WriteNumber("components", components);
        writer.WriteString("kind", "f32");
        writer.WriteEndObject();
    }

    private static void WriteVector3(Utf8JsonWriter writer, string name, NormalizedVector3 value)
    {
        writer.WritePropertyName(name);
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }

    private static void WriteVector3(Span<byte> output, ref int at, NormalizedVector3 value)
    {
        WriteSingle(output, ref at, value.X);
        WriteSingle(output, ref at, value.Y);
        WriteSingle(output, ref at, value.Z);
    }

    private static void WriteSingle(Span<byte> output, ref int at, float value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(output[at..], value);
        at += sizeof(float);
    }
}
