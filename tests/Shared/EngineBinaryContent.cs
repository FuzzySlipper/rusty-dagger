using System.Buffers.Binary;
using System.Text;
using System.Text.Json;

namespace WorldRpg.Tests.Support;

/// <summary>
/// Test-only readers of the Engine's binary content forms the importer publishes: the Spatial content
/// artifact (<c>RSPATIAL</c>) and the static mesh (<c>RSTATMSH</c>). The product never reads these
/// bytes; it hands their path and digest to the Engine. Tests read them back to state what was written.
/// </summary>
internal static class EngineBinaryContent
{
    internal sealed record SpatialCell(int Column, int Row, double SupportHeight, bool Walkable);

    internal sealed record SpatialArtifact(
        string StaticMeshArtifactId,
        string NavigationId,
        double[] Bounds,
        double[] Config,
        (double X, double Y, double Z)[] Positions,
        (uint A, uint B, uint C)[] Triangles,
        SpatialCell[] Cells);

    /// <summary>A binary static mesh: its descriptor document and the streams of its packed resource.</summary>
    internal sealed record StaticMesh(JsonDocument Descriptor, float[] Positions, float[] Normals, float[]? Uvs, uint[] Indices) : IDisposable
    {
        public JsonElement Root => Descriptor.RootElement;

        public void Dispose() => Descriptor.Dispose();
    }

    public static SpatialArtifact ReadSpatial(ReadOnlySpan<byte> bytes)
    {
        if (!bytes.StartsWith("RSPATIAL"u8)) throw new InvalidDataException("Not a binary Spatial content artifact.");
        int at = 8;
        at += 8; // schema versions
        double[] bounds = new double[6];
        for (int index = 0; index < 6; index++) bounds[index] = Double(bytes, ref at);
        double[] config = new double[5];
        for (int index = 0; index < 5; index++) config[index] = Double(bytes, ref at);
        string staticMesh = Text(bytes, ref at);
        string navigation = Text(bytes, ref at);
        int positions = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[at..]));
        int triangles = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(at + 8)..]));
        int cells = checked((int)BinaryPrimitives.ReadUInt64LittleEndian(bytes[(at + 16)..]));
        at += 24;
        (double, double, double)[] positionValues = new (double, double, double)[positions];
        for (int index = 0; index < positions; index++) positionValues[index] = (Double(bytes, ref at), Double(bytes, ref at), Double(bytes, ref at));
        (uint, uint, uint)[] triangleValues = new (uint, uint, uint)[triangles];
        for (int index = 0; index < triangles; index++) triangleValues[index] = (UInt32(bytes, ref at), UInt32(bytes, ref at), UInt32(bytes, ref at));
        int columns = at, rows = at + (cells * 4), heights = at + (cells * 8), walkable = at + (cells * 16);
        SpatialCell[] cellValues = new SpatialCell[cells];
        for (int index = 0; index < cells; index++)
        {
            cellValues[index] = new(
                BinaryPrimitives.ReadInt32LittleEndian(bytes[(columns + (index * 4))..]),
                BinaryPrimitives.ReadInt32LittleEndian(bytes[(rows + (index * 4))..]),
                BinaryPrimitives.ReadDoubleLittleEndian(bytes[(heights + (index * 8))..]),
                (bytes[walkable + (index / 8)] & (1 << (index % 8))) != 0);
        }
        if (walkable + ((cells + 7) / 8) != bytes.Length) throw new InvalidDataException("The binary Spatial artifact has trailing or missing bytes.");
        return new(staticMesh, navigation, bounds, config, positionValues, triangleValues, cellValues);
    }

    public static StaticMesh ReadStaticMesh(ReadOnlyMemory<byte> memory)
    {
        ReadOnlySpan<byte> bytes = memory.Span;
        if (!bytes.StartsWith("RSTATMSH"u8)) throw new InvalidDataException("Not a binary static mesh.");
        int length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]));
        JsonDocument descriptor = JsonDocument.Parse(memory.Slice(12, length));
        ReadOnlyMemory<byte> resourceMemory = memory[(12 + length)..];
        ReadOnlySpan<byte> resource = resourceMemory.Span;
        JsonElement payload = descriptor.RootElement.GetProperty("payload");
        JsonElement source = payload.GetProperty("source");
        int vertices = payload.GetProperty("layout").GetProperty("vertexCount").GetInt32();
        int indices = payload.GetProperty("layout").GetProperty("indexCount").GetInt32();
        if (source.GetProperty("byteLength").GetInt32() != resource.Length) throw new InvalidDataException("The packed mesh resource length differs from its descriptor.");
        float[] Floats(string offset, int count)
        {
            int start = source.GetProperty(offset).GetInt32();
            float[] values = new float[count];
            for (int index = 0; index < count; index++) values[index] = BinaryPrimitives.ReadSingleLittleEndian(resourceMemory.Span[(start + (index * 4))..]);
            return values;
        }
        uint[] indexValues = new uint[indices];
        int indexStart = source.GetProperty("indicesByteOffset").GetInt32();
        for (int index = 0; index < indices; index++) indexValues[index] = BinaryPrimitives.ReadUInt32LittleEndian(resource[(indexStart + (index * 4))..]);
        return new(
            descriptor,
            Floats("positionsByteOffset", vertices * 3),
            Floats("normalsByteOffset", vertices * 3),
            source.TryGetProperty("uvsByteOffset", out _) ? Floats("uvsByteOffset", vertices * 2) : null,
            indexValues);
    }

    private static double Double(ReadOnlySpan<byte> bytes, ref int at)
    {
        double value = BinaryPrimitives.ReadDoubleLittleEndian(bytes[at..]);
        at += 8;
        return value;
    }

    private static uint UInt32(ReadOnlySpan<byte> bytes, ref int at)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes[at..]);
        at += 4;
        return value;
    }

    private static string Text(ReadOnlySpan<byte> bytes, ref int at)
    {
        int length = checked((int)UInt32(bytes, ref at));
        string value = Encoding.UTF8.GetString(bytes.Slice(at, length));
        at += length;
        return value;
    }
}
