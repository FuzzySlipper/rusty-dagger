using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Source.Tool.Remodels;

namespace Daggerfall.Source.Tool.Commands;

/// <summary>
/// Writes an authored ARCH3D remodel (a GLB, <see cref="RemodelGlb"/>) as the Engine static mesh the art pack's
/// <c>models</c> section draws, with the importer's own writer, after holding it inside the classic model's volume
/// (<see cref="ClassicVolume"/>). It writes <c>OUTPUT/MODEL.rstatmsh</c> and each texture the GLB carries as
/// <c>OUTPUT/MODEL-N.png</c>, and prints the section's entry, its paths under <c>--content-path</c>, for
/// <c>authoring/tools/shared/promote.py --area models</c> to record: a slot drawing one layer of a shell stack states it
/// (<c>"shell": {"layer": K, "layers": N}</c>), and one drawing a strip of frames or unlit states that (<c>"frames"</c>,
/// <c>"framesPerSecond"</c>, <c>"unlit": true</c>). A remodel that pokes out, or a stack missing a layer, is refused by name.
/// </summary>
internal static class ModelMeshCommand
{
    public static ToolCommand Command { get; } = new("model-mesh",
    [
        Options.Arena2,
        CommandOption.Required("--model", "ARCH3D_ID"),
        CommandOption.Required("--glb", "FILE"),
        CommandOption.Required("--output", "DIR"),
        CommandOption.Required("--content-path", "PATH"),
        CommandOption.Optional("--fit", "hull|collision"),
    ], Run, Program.Tool);

    private static int Run(CommandArguments args)
    {
        string model = args["--model"];
        if (!uint.TryParse(model, NumberStyles.None, CultureInfo.InvariantCulture, out uint number))
            throw new ArgumentException($"--model {model} is not an ARCH3D record id.");
        RemodelFit fit = args.Optional("--fit") switch
        {
            null or "hull" => RemodelFit.Hull,
            "collision" => RemodelFit.Collision,
            string other => throw new ArgumentException($"--fit {other} is neither hull nor collision."),
        };
        IReadOnlyList<RemodelPart> parts = RemodelGlb.Read(File.ReadAllBytes(args["--glb"]), args["--glb"]);
        ClassicVolume.Read(args["--arena2"], number).Require([.. parts.SelectMany(part => part.Corners.Chunk(3)).Select(face => (face[0].Position, face[1].Position, face[2].Position))], fit, model);
        (byte[] mesh, IReadOnlyList<(string Material, int Slot, RemodelShell? Shell, (string Identity, IReadOnlyDictionary<string, string> Drawn)? Paint)> slots, int triangles,
            IReadOnlyDictionary<string, (byte[] Png, bool Mask, RemodelStrip? Strip, bool Unlit)> textures) = Write(model, parts, args["--content-path"]);

        string output = args["--output"];
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, $"{model}.rstatmsh"), mesh);
        foreach ((string path, (byte[] png, _, _, _)) in textures) File.WriteAllBytes(Path.Combine(output, Path.GetFileName(path)), png);
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["path"] = $"{args["--content-path"]}/{model}.rstatmsh",
            ["byteLength"] = mesh.Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(mesh)),
            ["triangles"] = triangles,
            ["slots"] = slots.Select(slot => textures.TryGetValue(slot.Material, out (byte[] Png, bool Mask, RemodelStrip? Strip, bool Unlit) texture)
                ? new Dictionary<string, object> { ["slot"] = slot.Slot, ["texture"] = Published(slot.Material) }
                    .Concat(texture.Mask ? [KeyValuePair.Create("alpha", (object)"mask")] : [])
                    .Concat(texture.Strip is { } strip ? [KeyValuePair.Create("frames", (object)strip.Frames), KeyValuePair.Create("framesPerSecond", (object)strip.FramesPerSecond)] : [])
                    .Concat(texture.Unlit ? [KeyValuePair.Create("unlit", (object)true)] : [])
                    .Concat(slot.Paint is { } paint ? [KeyValuePair.Create("material", (object)paint.Identity)] : [])
                    .Concat(slot.Paint is { Drawn.Count: > 0 } painted
                        ? [KeyValuePair.Create("drawn", (object)painted.Drawn.ToDictionary(pair => pair.Key, pair => Published(pair.Value)))] : [])
                    .ToDictionary()
                : new Dictionary<string, object> { ["slot"] = slot.Slot, ["material"] = slot.Material }
                    .Concat(slot.Shell is { } shell ? [KeyValuePair.Create("shell", (object)new Dictionary<string, int> { ["layer"] = shell.Layer, ["layers"] = shell.Layers })] : [])
                    .ToDictionary()).ToArray(),
        }));
        return 0;

        Dictionary<string, object> Published(string path) =>
            new() { ["path"] = path, ["byteLength"] = textures[path].Png.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(textures[path].Png)) };
    }

    /// <summary>
    /// The remodel as the importer's static mesh: one material group per material, a classic one named by its texture
    /// identity (and a shell layer's by its layer too) and one the GLB carries by the content path of its PNG; its slots as
    /// the mesh numbers them, each with the classic texture or PNG it draws, the shell layer it is, and for a PNG painted
    /// for a classic identity that identity and the path of its PNG for each other identity it may be drawn as; and its
    /// PNGs, each opaque or cut out, still or a strip of frames, lit or not. A classic texture drawn in shells has every
    /// layer of one stack.
    /// </summary>
    internal static (byte[] Mesh, IReadOnlyList<(string Material, int Slot, RemodelShell? Shell, (string Identity, IReadOnlyDictionary<string, string> Drawn)? Paint)> Slots, int Triangles, IReadOnlyDictionary<string, (byte[] Png, bool Mask, RemodelStrip? Strip, bool Unlit)> Textures) Write(
        string model, IReadOnlyList<RemodelPart> parts, string contentPath)
    {
        foreach (IGrouping<string, RemodelShell> stack in parts.Where(part => part.Shell is not null).GroupBy(part => part.ClassicMaterial!, part => part.Shell!, StringComparer.Ordinal))
        {
            int layers = stack.First().Layers;
            if (stack.Any(shell => shell.Layers != layers) || !stack.Select(shell => shell.Layer).Distinct().Order().SequenceEqual(Enumerable.Range(0, layers)))
                throw new InvalidOperationException($"The remodel of ARCH3D model {model} draws {stack.Key} in shell layers {string.Join(", ", stack.Select(shell => $"{shell.Layer} of {shell.Layers}").Distinct())}: a stack has each of its layers.");
        }

        Dictionary<string, (byte[] Png, bool Mask, RemodelStrip? Strip, bool Unlit)> textures = new(StringComparer.Ordinal);
        List<NormalizedVector3> vertices = [], normals = [];
        List<NormalizedVector2> uvs = [];
        List<NormalizedTriangle> triangles = [];
        List<NormalizedMaterialGroup> groups = [];
        Dictionary<string, (string Material, RemodelShell? Shell)> drawn = new(StringComparer.Ordinal);
        Dictionary<string, (string Identity, IReadOnlyDictionary<string, string> Drawn)> painted = new(StringComparer.Ordinal);
        foreach (IGrouping<string, RemodelPart> material in parts.GroupBy(Material, StringComparer.Ordinal))
        {
            int start = triangles.Count;
            foreach ((Vector3 position, Vector3 normal, Vector2 uv) in material.SelectMany(part => part.Corners))
            {
                if (vertices.Count % 3 == 0) triangles.Add(new(vertices.Count, vertices.Count + 1, vertices.Count + 2));
                vertices.Add(new(position.X, position.Y, position.Z));
                normals.Add(new(normal.X, normal.Y, normal.Z));
                uvs.Add(new(uv.X, uv.Y));
            }

            groups.Add(new NormalizedMaterialGroup(material.Key, start, triangles.Count - start, false));
        }

        NormalizedMesh mesh = new NormalizedMesh($"remodel-{model}", $"remodel/{model}", vertices, normals, uvs, triangles, groups).Canonicalize();
        mesh.Validate();
        MeshAssembly assembly = MeshAssembly.Create([mesh]);
        return (StaticMeshArtifact.Serialize($"remodel-{model}", MeshGeometry.Bounds(vertices), assembly),
            [.. assembly.MaterialSlots.Select(slot => (drawn[slot.Material].Material, slot.Slot, drawn[slot.Material].Shell,
                painted.TryGetValue(slot.Material, out var paint) ? paint : ((string, IReadOnlyDictionary<string, string>)?)null))], triangles.Count, textures);

        // The mesh names a shell layer's group by its texture and layer, so each layer is a slot of its own; a PNG painted
        // for a classic identity, by its PNG, with the identity and its other PNGs beside it.
        string Material(RemodelPart part)
        {
            string material = part.Png is null ? part.ClassicMaterial! : Texture(part.Png, part.Mask, part.Strip, part.Unlit);
            if (part is { ClassicMaterial: { } identity, Png: not null })
                painted.TryAdd(material, (identity, (part.Drawn ?? new Dictionary<string, byte[]>()).ToDictionary(pair => pair.Key, pair => Texture(pair.Value, false), StringComparer.Ordinal)));
            string name = part.Shell is { } shell ? string.Create(CultureInfo.InvariantCulture, $"{material}/shell-{shell.Layer}-of-{shell.Layers}") : material;
            drawn.TryAdd(name, (material, part.Shell));
            return name;
        }

        // A carried texture is published beside the mesh, numbered in the order the GLB's materials name them; one drawn
        // two ways (opaque and cut out, or as a strip and still) is published once for each.
        string Texture(byte[] png, bool mask, RemodelStrip? strip = null, bool unlit = false)
        {
            string? known = textures.FirstOrDefault(pair => (pair.Value.Mask, pair.Value.Strip, pair.Value.Unlit) == (mask, strip, unlit) && pair.Value.Png.AsSpan().SequenceEqual(png)).Key;
            if (known is not null) return known;
            string path = string.Create(CultureInfo.InvariantCulture, $"{contentPath}/{model}-{textures.Count}.png");
            textures.Add(path, (png, mask, strip, unlit));
            return path;
        }
    }
}
