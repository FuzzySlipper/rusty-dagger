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
/// <c>authoring/tools/shared/promote.py --area models</c> to record. A remodel that pokes out is refused by name.
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
        (byte[] mesh, IReadOnlyList<(string Material, int Slot)> slots, int triangles, IReadOnlyDictionary<string, (byte[] Png, bool Mask)> textures) = Write(model, parts, args["--content-path"]);

        string output = args["--output"];
        Directory.CreateDirectory(output);
        File.WriteAllBytes(Path.Combine(output, $"{model}.rstatmsh"), mesh);
        foreach ((string path, (byte[] png, bool _)) in textures) File.WriteAllBytes(Path.Combine(output, Path.GetFileName(path)), png);
        Console.WriteLine(JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["path"] = $"{args["--content-path"]}/{model}.rstatmsh",
            ["byteLength"] = mesh.Length,
            ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(mesh)),
            ["triangles"] = triangles,
            ["slots"] = slots.Select(slot => textures.TryGetValue(slot.Material, out (byte[] Png, bool Mask) texture)
                ? new Dictionary<string, object>
                {
                    ["slot"] = slot.Slot,
                    ["texture"] = new Dictionary<string, object> { ["path"] = slot.Material, ["byteLength"] = texture.Png.Length, ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(texture.Png)) },
                }.Concat(texture.Mask ? [KeyValuePair.Create("alpha", (object)"mask")] : []).ToDictionary()
                : new Dictionary<string, object> { ["slot"] = slot.Slot, ["material"] = slot.Material }).ToArray(),
        }));
        return 0;
    }

    /// <summary>
    /// The remodel as the importer's static mesh: one material group per material, a classic one named by its texture
    /// identity and one the GLB carries by the content path of its PNG; its slots as the mesh numbers them; and its PNGs,
    /// each opaque or cut out.
    /// </summary>
    internal static (byte[] Mesh, IReadOnlyList<(string Material, int Slot)> Slots, int Triangles, IReadOnlyDictionary<string, (byte[] Png, bool Mask)> Textures) Write(
        string model, IReadOnlyList<RemodelPart> parts, string contentPath)
    {
        Dictionary<string, (byte[] Png, bool Mask)> textures = new(StringComparer.Ordinal);
        List<NormalizedVector3> vertices = [], normals = [];
        List<NormalizedVector2> uvs = [];
        List<NormalizedTriangle> triangles = [];
        List<NormalizedMaterialGroup> groups = [];
        foreach (IGrouping<string, RemodelPart> material in parts.GroupBy(part => part.ClassicMaterial ?? Texture(part.Png!, part.Mask), StringComparer.Ordinal))
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
        return (StaticMeshArtifact.Serialize($"remodel-{model}", MeshGeometry.Bounds(vertices), assembly), assembly.MaterialSlots, triangles.Count, textures);

        // A carried texture is published beside the mesh, numbered in the order the GLB's materials name them; one drawn
        // both opaque and cut out is published once for each.
        string Texture(byte[] png, bool mask)
        {
            string? known = textures.FirstOrDefault(pair => pair.Value.Mask == mask && pair.Value.Png.AsSpan().SequenceEqual(png)).Key;
            if (known is not null) return known;
            string path = string.Create(CultureInfo.InvariantCulture, $"{contentPath}/{model}-{textures.Count}.png");
            textures.Add(path, (png, mask));
            return path;
        }
    }
}
