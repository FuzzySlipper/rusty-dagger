using System.Text.Json;
using System.Text.Json.Nodes;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>Admits authored normalized block variants into the same publication as their base blocks.</summary>
public static class WorldBlockVariantPublication
{
    public static WorldBlocksPublication Append(WorldBlocksPublication publication, string descriptorPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(descriptorPath))!;
        byte[] descriptor = File.ReadAllBytes(descriptorPath);
        JsonObject authored = JsonNode.Parse(descriptor)!.AsObject();
        JsonArray variants = authored["variants"]?.AsArray() ?? throw new InvalidOperationException("Block variant descriptor requires variants.");
        var artifacts = publication.Plan.Artifacts.Where(value => value.RelativePath is not ("blocks.json" or "import-manifest.json")).ToDictionary(value => value.RelativePath, StringComparer.Ordinal);
        List<PublishedSource> overlays = [PublishedSource.Of("world-block-variants/index.json", descriptor)];
        HashSet<string> meshes = [.. publication.MeshArtifactIds];
        HashSet<string> selections = [];
        foreach (JsonNode? node in variants)
        {
            JsonObject entry = node!.AsObject();
            string path = entry["document"]!.GetValue<string>();
            byte[] bytes = Read(path);
            var block = JsonSerializer.Deserialize<DaggerfallWorldBlock>(bytes, PublishedJson.Section)
                ?? throw new InvalidOperationException($"Variant document '{path}' is null.");
            var basis = publication.Index.Blocks.SingleOrDefault(value => value.Kind == block.Kind && value.SourceKey == block.SourceKey && value.BuildingIndex == block.BuildingIndex)
                ?? throw new InvalidOperationException($"Variant '{path}' names a block absent from the base publication.");
            string? variant = entry["variant"]?.GetValue<string>();
            if (variant is not null) RequireVariant(variant);
            Dictionary<int, string> buildings = [];
            foreach (JsonNode? value in entry["buildingVariants"]?.AsArray() ?? [])
            {
                int building = value!["building"]!.GetValue<int>();
                string name = value["variant"]!.GetValue<string>();
                RequireVariant(name);
                if (building < 0 || !buildings.TryAdd(building, name)) throw new InvalidOperationException($"Variant '{path}' repeats or misnumbers a building.");
            }
            if (variant is null && buildings.Count == 0 || block.Kind == DaggerfallWorldBlockKind.Rdb && buildings.Count != 0
                || block.Kind == DaggerfallWorldBlockKind.RmbInterior && buildings.Keys.Any(value => value != block.BuildingIndex))
                throw new InvalidOperationException($"Variant '{path}' has an invalid block/building selection.");
            string selection = $"{basis.Key}|{variant}|" + string.Join(";", buildings.OrderBy(value => value.Key).Select(value => $"{value.Key}:{value.Value}"));
            if (!selections.Add(selection)) throw new InvalidOperationException($"Repeated block variant '{selection}'.");
            // Identity and spatial facts come from the normalized document, never a second editable copy.
            entry["key"] = block.Key;
            entry["kind"] = JsonSerializer.SerializeToNode(block.Kind, PublishedJson.Section);
            entry["sourceKey"] = block.SourceKey;
            entry["buildingIndex"] = block.BuildingIndex;
            entry["spatial"] = block.Spatial?.RelativePath;
            Add(path, bytes, block.Spatial is { } spatial ? [spatial.RelativePath] : []);
            if (block.Spatial is { } collision)
            {
                byte[] body = Read(collision.RelativePath);
                if (ContentDigest.Compute(body) != collision.ContentDigest || body.LongLength != collision.ByteLength)
                    throw new InvalidOperationException($"Variant '{path}' spatial bytes do not match its normalized digest and length.");
                Add(collision.RelativePath, body, []);
            }
            foreach (var model in block.Models) if (model.MeshArtifactId is { } id) meshes.Add(id);
        }
        JsonObject index = JsonSerializer.SerializeToNode(publication.Index, PublishedJson.Section)!.AsObject();
        index["variants"] = variants.DeepClone();
        artifacts.Add("blocks.json", new("blocks.json", JsonSerializer.SerializeToUtf8Bytes(index, PublishedJson.SectionCompact),
            [.. publication.Index.Blocks.Select(value => value.Document), .. variants.Select(value => value!["document"]!.GetValue<string>())]));
        var manifest = publication.Plan.Manifest;
        var provenance = new ImportProvenance(manifest.ImporterId, manifest.ImporterRevision,
            [.. manifest.Sources.Select(value => new LogicalSourceRecord(value.Path, value.ContentDigest, value.ByteLength))]);
        var plan = ImportPublicationPlan.Create(provenance, artifacts.Values, new(manifest.Command, [.. manifest.AuthoredOverlays, .. overlays]));
        return publication with { Plan = plan, MeshArtifactIds = [.. meshes.Order(StringComparer.Ordinal)] };

        byte[] Read(string relative)
        {
            NormalizedImportDocument.RequireLogicalPath(relative, nameof(relative));
            return File.ReadAllBytes(Path.Combine(directory, relative));
        }
        void Add(string path, byte[] bytes, string[] dependencies)
        {
            if (artifacts.TryGetValue(path, out var existing))
            {
                if (!existing.Bytes.Span.SequenceEqual(bytes)) throw new InvalidOperationException($"Variant artifact '{path}' collides with another publication artifact.");
                return;
            }
            artifacts.Add(path, new(path, bytes, dependencies));
            overlays.Add(PublishedSource.Of("world-block-variants/" + path, bytes));
        }
        static void RequireVariant(string name)
        {
            NormalizedImportDocument.RequireLogicalId(name, nameof(name));
            if (name == "-" || name.Contains(':') || name.Contains(';') || name.Contains('|'))
                throw new InvalidOperationException($"Invalid block variant name '{name}'.");
        }
    }
}
