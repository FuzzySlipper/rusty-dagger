using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using Daggerfall.Import.Normalization;

namespace Daggerfall.Source.Tool.Remodels;

/// <summary>
/// One material of an authored remodel: the triangles it draws, each corner's position, normal and uv in the classic
/// model's frame, and what it draws them with: a classic texture identity, or else the PNG the GLB carries for it, cut
/// out where its alpha is under half when <paramref name="Mask"/>. A classic texture may be drawn as one layer of a
/// shell stack (<paramref name="Shell"/>). With both an identity and a PNG it is painted for that identity, and
/// <paramref name="Drawn"/> holds the PNG painted for each other identity a location may draw it as. A PNG of its own may
/// be a <paramref name="Strip"/> of frames the game steps through, and may be drawn <paramref name="Unlit"/>, at its own
/// colours whatever the light, as a hearth's flames are.
/// </summary>
internal sealed record RemodelPart(string? ClassicMaterial, byte[]? Png, IReadOnlyList<(Vector3 Position, Vector3 Normal, Vector2 Uv)> Corners, bool Mask = false,
    RemodelShell? Shell = null, IReadOnlyDictionary<string, byte[]>? Drawn = null, RemodelStrip? Strip = null, bool Unlit = false);

/// <summary>
/// A texture of a remodel's own that is a strip of <paramref name="Frames"/> equal frames side by side, left to right, which
/// the game shows one at a time at <paramref name="FramesPerSecond"/> on faces whose uvs cover one frame's width of [0, 1].
/// </summary>
internal sealed record RemodelStrip(int Frames, float FramesPerSecond);

/// <summary>
/// One layer of a shell stack drawing a classic texture's relief, whose alpha is each texel's height: layer 0 is the
/// stack's core, drawn whole, and layer K of N shows the texels at least K/N tall, so a texel's column rises from the core
/// to the outermost layer.
/// </summary>
internal sealed record RemodelShell(int Layer, int Layers);

/// <summary>
/// Reads an authored remodel from a binary glTF (GLB): every triangle of the default scene, placed by its nodes'
/// transforms, grouped by material. The GLB is in the classic model's own frame: importer space, right-handed metres with
/// Y up, as glTF is. A material named <c>texture-A-R</c> (or <c>material/texture-A-R</c>) draws that classic texture
/// record, its uvs in whole repeats of the record with v down, as glTF's are, and one whose glTF <c>extras</c> state
/// <c>"shell": {"layer": K, "layers": N}</c> draws it as layer K of an N-layer shell stack (<see cref="RemodelShell"/>), the
/// slot's own <c>shell</c> in the art pack. A classic-named material that carries a base colour too is painted for that
/// identity: its PNG is drawn in place of the location's surface, and its <c>extras.drawn</c> names, by identity, the
/// texture painted for each other identity the location may draw it as (a stone's climate swaps), the slot's own
/// <c>drawn</c>. Any other material carries its own PNG base
/// colour, opaque or, with glTF's <c>alphaMode</c> <c>MASK</c> at the default cutoff of half, cut out by its alpha, a strip of
/// frames where its <c>extras</c> state <c>frames</c> and <c>framesPerSecond</c> (<see cref="RemodelStrip"/>), and unlit under
/// glTF's <c>KHR_materials_unlit</c>. That PNG
/// is written again as 8-bit RGBA, the only PNG the Engine admits as a texture, whatever 8-bit truecolour form the GLB
/// carries it in. Only what an authoring tool writes for a static mesh is read; anything else is refused by name.
/// </summary>
internal static class RemodelGlb
{
    private const uint Magic = 0x46546C67, JsonChunk = 0x4E4F534A, BinaryChunk = 0x004E4942;

    internal static IReadOnlyList<RemodelPart> Read(byte[] glb, string label)
    {
        if (glb.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian(glb) != Magic || BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(4)) != 2)
            throw new InvalidDataException($"{label} is not a glTF 2 binary.");
        int jsonLength = BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));
        if (BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(16)) != JsonChunk) throw new InvalidDataException($"{label} does not start with its JSON chunk.");
        using JsonDocument document = JsonDocument.Parse(glb.AsMemory(20, jsonLength));
        int binaryAt = 20 + jsonLength;
        ReadOnlyMemory<byte> binary = binaryAt + 8 <= glb.Length && BinaryPrimitives.ReadUInt32LittleEndian(glb.AsSpan(binaryAt + 4)) == BinaryChunk
            ? glb.AsMemory(binaryAt + 8, BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(binaryAt)))
            : ReadOnlyMemory<byte>.Empty;
        return new Reader(document.RootElement, binary, label).Parts();
    }

    private sealed class Reader(JsonElement root, ReadOnlyMemory<byte> binary, string label)
    {
        private readonly Dictionary<int, List<(Vector3, Vector3, Vector2)>> _byMaterial = [];

        internal IReadOnlyList<RemodelPart> Parts()
        {
            int scene = root.TryGetProperty("scene", out JsonElement chosen) ? chosen.GetInt32() : 0;
            if (!root.TryGetProperty("scenes", out JsonElement scenes) || scene >= scenes.GetArrayLength())
                throw new InvalidDataException($"{label} names no scene to read.");
            foreach (JsonElement node in scenes[scene].GetProperty("nodes").EnumerateArray()) Node(node.GetInt32(), Matrix4x4.Identity);
            if (_byMaterial.Count == 0) throw new InvalidDataException($"{label} draws no triangle.");
            return [.. _byMaterial.OrderBy(pair => pair.Key).Select(pair => Part(pair.Key, pair.Value))];
        }

        private void Node(int index, Matrix4x4 parent)
        {
            JsonElement node = root.GetProperty("nodes")[index];
            Matrix4x4 local = Matrix4x4.Identity;
            if (node.TryGetProperty("matrix", out JsonElement matrix))
            {
                // glTF's column-major array is System.Numerics' row-vector matrix in reading order.
                float[] m = [.. matrix.EnumerateArray().Select(value => value.GetSingle())];
                local = new(m[0], m[1], m[2], m[3], m[4], m[5], m[6], m[7], m[8], m[9], m[10], m[11], m[12], m[13], m[14], m[15]);
            }
            else
            {
                Vector3 scale = node.TryGetProperty("scale", out JsonElement s) ? Vector(s) : Vector3.One;
                Quaternion rotation = node.TryGetProperty("rotation", out JsonElement r)
                    ? new(r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle()) : Quaternion.Identity;
                Vector3 translation = node.TryGetProperty("translation", out JsonElement t) ? Vector(t) : Vector3.Zero;
                local = Matrix4x4.CreateScale(scale) * Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation);
            }

            Matrix4x4 world = local * parent;
            if (node.TryGetProperty("mesh", out JsonElement mesh)) Mesh(mesh.GetInt32(), world);
            if (node.TryGetProperty("children", out JsonElement children))
                foreach (JsonElement child in children.EnumerateArray()) Node(child.GetInt32(), world);
        }

        private void Mesh(int index, Matrix4x4 world)
        {
            if (!Matrix4x4.Invert(world, out Matrix4x4 inverse)) throw new InvalidDataException($"{label} places mesh {index} by a transform that flattens it.");
            Matrix4x4 normals = Matrix4x4.Transpose(inverse);
            // A mirroring transform turns the faces inside out, so their corners are taken in the other order.
            bool mirrored = world.GetDeterminant() < 0;
            foreach (JsonElement primitive in root.GetProperty("meshes")[index].GetProperty("primitives").EnumerateArray())
            {
                if (primitive.TryGetProperty("mode", out JsonElement mode) && mode.GetInt32() != 4)
                    throw new InvalidDataException($"{label} draws mesh {index} with primitive mode {mode.GetInt32()}, not triangles.");
                if (primitive.TryGetProperty("extensions", out _)) throw new InvalidDataException($"{label} compresses or extends mesh {index}, which is not read.");
                JsonElement attributes = primitive.GetProperty("attributes");
                if (!attributes.TryGetProperty("NORMAL", out JsonElement normal) || !attributes.TryGetProperty("TEXCOORD_0", out JsonElement uv))
                    throw new InvalidDataException($"{label} draws mesh {index} without normals or uvs.");
                if (!primitive.TryGetProperty("material", out JsonElement material))
                    throw new InvalidDataException($"{label} draws mesh {index} without a material.");
                Vector3[] positions = [.. Floats(attributes.GetProperty("POSITION").GetInt32(), 3).Select(value => Vector3.Transform(new Vector3(value[0], value[1], value[2]), world))];
                Vector3[] facing = [.. Floats(normal.GetInt32(), 3).Select(value => Vector3.Normalize(Vector3.TransformNormal(new Vector3(value[0], value[1], value[2]), normals)))];
                // A normal of no length comes out of its normalizing as no number at all, which the Engine refuses in the
                // static mesh, faulting the location that draws it (#10871).
                if (Array.FindIndex(facing, n => !float.IsFinite(n.X) || !float.IsFinite(n.Y) || !float.IsFinite(n.Z)) is int wrong and >= 0)
                    throw new InvalidDataException($"{label} draws mesh {index} with a normal of no length or not finite, at vertex {wrong}.");
                Vector2[] uvs = [.. Floats(uv.GetInt32(), 2).Select(value => new Vector2(value[0], value[1]))];
                uint[] indices = primitive.TryGetProperty("indices", out JsonElement indexAccessor) ? Indices(indexAccessor.GetInt32()) : [.. Enumerable.Range(0, positions.Length).Select(i => (uint)i)];
                if (indices.Length % 3 != 0 || indices.Any(corner => corner >= positions.Length))
                    throw new InvalidDataException($"{label} draws mesh {index} with indices that are not whole triangles of its vertices.");
                if (!_byMaterial.TryGetValue(material.GetInt32(), out List<(Vector3, Vector3, Vector2)>? corners)) _byMaterial[material.GetInt32()] = corners = [];
                for (int at = 0; at < indices.Length; at += 3)
                    foreach (int corner in mirrored ? new[] { 0, 2, 1 } : [0, 1, 2])
                    {
                        uint vertex = indices[at + corner];
                        corners.Add((positions[vertex], facing[vertex], uvs[vertex]));
                    }
            }
        }

        private RemodelPart Part(int index, List<(Vector3, Vector3, Vector2)> corners)
        {
            JsonElement material = root.GetProperty("materials")[index];
            string name = material.TryGetProperty("name", out JsonElement named) ? named.GetString() ?? "" : "";
            string alpha = material.TryGetProperty("alphaMode", out JsonElement mode) ? mode.GetString() ?? "OPAQUE" : "OPAQUE";
            if (alpha is not ("OPAQUE" or "MASK") || material.TryGetProperty("alphaCutoff", out JsonElement cutoff) && cutoff.GetSingle() != 0.5F)
                throw new InvalidDataException($"{label}'s material '{name}' is drawn {alpha} with its own cutoff: a remodel is opaque, or cut out at half its alpha (MASK).");
            RemodelShell? shell = material.TryGetProperty("extras", out JsonElement extras) && extras.TryGetProperty("shell", out JsonElement stack)
                ? new(stack.GetProperty("layer").GetInt32(), stack.GetProperty("layers").GetInt32()) : null;
            if (shell is not null && (shell.Layers is < 2 or > 16 || shell.Layer < 0 || shell.Layer >= shell.Layers))
                throw new InvalidDataException($"{label}'s material '{name}' is no layer of a shell stack of 2 to 16.");
            RemodelStrip? strip = extras.ValueKind == JsonValueKind.Object && (extras.TryGetProperty("frames", out JsonElement frames) | extras.TryGetProperty("framesPerSecond", out JsonElement rate))
                ? new(frames.ValueKind == JsonValueKind.Number ? frames.GetInt32() : 0, rate.ValueKind == JsonValueKind.Number ? rate.GetSingle() : 0F) : null;
            if (strip is not null && (strip.Frames < 2 || !float.IsFinite(strip.FramesPerSecond) || strip.FramesPerSecond <= 0F))
                throw new InvalidDataException($"{label}'s material '{name}' states {strip.Frames} frames at {strip.FramesPerSecond} a second: a strip is two frames or more at a rate above zero.");
            bool unlit = material.TryGetProperty("extensions", out JsonElement extensions) && extensions.TryGetProperty("KHR_materials_unlit", out _);
            JsonElement texture = default, others = default;
            bool carries = material.TryGetProperty("pbrMetallicRoughness", out JsonElement pbr) && pbr.TryGetProperty("baseColorTexture", out texture);
            bool paints = extras.ValueKind == JsonValueKind.Object && extras.TryGetProperty("drawn", out others);
            if (Identity(name) is { } classic)
            {
                // A classic identity draws as the location's own lit surface, painted or not, where its climate swaps follow it.
                if (strip is not null || unlit)
                    throw new InvalidDataException($"{label}'s material '{name}' animates or unlights a classic texture, which draws as the location's own lit surface.");
                if (alpha == "MASK")
                    throw new InvalidDataException($"{label}'s material '{name}' cuts out a classic texture, which draws the location's own opaque surface or its shells' own cut.");
                // Textures by drawn identity with none of its own would be lost, and the location's own surface drawn.
                if (!carries)
                    return paints ? throw new InvalidDataException($"{label}'s material '{name}' states textures by drawn identity but carries no base colour texture of its own.")
                        : new(classic, null, corners, Shell: shell);
                // Painted for its identity: a shell stack draws the location's relief, never a texture of the pack's own.
                if (shell is not null)
                    throw new InvalidDataException($"{label}'s material '{name}' stacks shells of a texture of its own: a shell stack draws a classic texture's relief.");
                Dictionary<string, byte[]> drawn = new(StringComparer.Ordinal);
                if (paints && others.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException($"{label}'s material '{name}' states its drawn textures as no map of classic texture to texture index.");
                if (paints)
                    foreach (JsonProperty other in others.EnumerateObject())
                    {
                        string identity = Identity(other.Name) ?? throw new InvalidDataException($"{label}'s material '{name}' is painted for '{other.Name}', which is no classic texture (texture-A-R).");
                        if (other.Value.ValueKind != JsonValueKind.Number || !other.Value.TryGetInt32(out int at))
                            throw new InvalidDataException($"{label}'s material '{name}' names its texture for '{other.Name}' by no texture index.");
                        if (!drawn.TryAdd(identity, Png(at, $"{name}' for '{other.Name}")))
                            throw new InvalidDataException($"{label}'s material '{name}' is painted twice for '{identity}'.");
                    }
                return new(classic, Png(texture.GetProperty("index").GetInt32(), name), corners, Drawn: drawn);
            }
            if (shell is not null)
                throw new InvalidDataException($"{label}'s material '{name}' stacks shells of a texture of its own: a shell stack draws a classic texture's relief.");
            if (paints)
                throw new InvalidDataException($"{label}'s material '{name}' states textures by drawn identity but names no classic texture (texture-A-R) to be painted for.");
            if (!carries)
                throw new InvalidDataException($"{label}'s material '{name}' names no classic texture (texture-A-R) and carries no base colour texture.");
            byte[] png = Png(texture.GetProperty("index").GetInt32(), name);
            int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16));
            if (strip is not null && width % strip.Frames != 0)
                throw new InvalidDataException($"{label}'s material '{name}' is a strip of {strip.Frames} frames {width} pixels wide, which they do not divide.");
            return new(null, png, corners, alpha == "MASK", Strip: strip, Unlit: unlit);
        }

        private static string? Identity(string name) =>
            System.Text.RegularExpressions.Regex.Match(name, @"^(?:material/)?texture-(\d+)-(\d+)$") is { Success: true } classic
                ? $"material/texture-{classic.Groups[1].Value}-{classic.Groups[2].Value}" : null;

        /// <summary>The PNG a texture of the GLB carries, written again as 8-bit RGBA.</summary>
        private byte[] Png(int index, string name)
        {
            if (!root.TryGetProperty("textures", out JsonElement textures) || index < 0 || index >= textures.GetArrayLength())
                throw new InvalidDataException($"{label}'s material '{name}' names texture {index}, which the GLB does not carry.");
            JsonElement image = root.GetProperty("images")[textures[index].GetProperty("source").GetInt32()];
            if (!image.TryGetProperty("bufferView", out JsonElement view) || image.GetProperty("mimeType").GetString() != "image/png")
                throw new InvalidDataException($"{label}'s material '{name}' carries a texture that is not a PNG inside the GLB.");
            DeterministicPngImage pixels = DeterministicPngReader.ReadRgba8(View(view.GetInt32()).ToArray(), $"{label}'s material '{name}' texture");
            return DeterministicPngEncoder.EncodeRgba8(pixels.Width, pixels.Height, pixels.Rgba);
        }

        private ReadOnlyMemory<byte> View(int index)
        {
            JsonElement view = root.GetProperty("bufferViews")[index];
            if (view.GetProperty("buffer").GetInt32() != 0) throw new InvalidDataException($"{label} keeps data outside its binary chunk.");
            int offset = view.TryGetProperty("byteOffset", out JsonElement at) ? at.GetInt32() : 0;
            return binary.Slice(offset, view.GetProperty("byteLength").GetInt32());
        }

        /// <summary>An accessor's float vectors of <paramref name="components"/> each.</summary>
        private IEnumerable<float[]> Floats(int index, int components)
        {
            JsonElement accessor = root.GetProperty("accessors")[index];
            if (accessor.GetProperty("componentType").GetInt32() != 5126 || accessor.TryGetProperty("sparse", out _))
                throw new InvalidDataException($"{label}'s accessor {index} is not plain 32-bit floats.");
            (ReadOnlyMemory<byte> data, int stride) = Elements(accessor, components * sizeof(float));
            int count = accessor.GetProperty("count").GetInt32();
            for (int element = 0; element < count; element++)
            {
                float[] value = new float[components];
                for (int component = 0; component < components; component++)
                    value[component] = BinaryPrimitives.ReadSingleLittleEndian(data.Span[(element * stride + component * sizeof(float))..]);
                yield return value;
            }
        }

        private uint[] Indices(int index)
        {
            JsonElement accessor = root.GetProperty("accessors")[index];
            int width = accessor.GetProperty("componentType").GetInt32() switch { 5121 => 1, 5123 => 2, 5125 => 4, int other => throw new InvalidDataException($"{label}'s index accessor {index} has component type {other}.") };
            (ReadOnlyMemory<byte> data, int stride) = Elements(accessor, width);
            uint[] values = new uint[accessor.GetProperty("count").GetInt32()];
            for (int element = 0; element < values.Length; element++)
            {
                ReadOnlySpan<byte> at = data.Span[(element * stride)..];
                values[element] = width switch { 1 => at[0], 2 => BinaryPrimitives.ReadUInt16LittleEndian(at), _ => BinaryPrimitives.ReadUInt32LittleEndian(at) };
            }

            return values;
        }

        private (ReadOnlyMemory<byte> Data, int Stride) Elements(JsonElement accessor, int size)
        {
            ReadOnlyMemory<byte> view = View(accessor.GetProperty("bufferView").GetInt32());
            int offset = accessor.TryGetProperty("byteOffset", out JsonElement at) ? at.GetInt32() : 0;
            JsonElement description = root.GetProperty("bufferViews")[accessor.GetProperty("bufferView").GetInt32()];
            int stride = description.TryGetProperty("byteStride", out JsonElement step) ? step.GetInt32() : size;
            return (view[offset..], stride);
        }

        private static Vector3 Vector(JsonElement value) => new(value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle());
    }
}
