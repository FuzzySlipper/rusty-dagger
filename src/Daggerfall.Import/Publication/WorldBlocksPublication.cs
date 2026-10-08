using System.Text.Json;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;

namespace Daggerfall.Import.Publication;

/// <summary>The world block publication, the index it writes and the product-wide mesh artifacts its models place.</summary>
public sealed record WorldBlocksPublication(ImportPublicationPlan Plan, DaggerfallWorldBlockIndex Index, IReadOnlyList<string> MeshArtifactIds);

/// <summary>
/// Publishes every block a location places as normalized per-block facts: each RMB block's exterior, each
/// building interior it declares, and each RDB block, with its collision/navigation artifact. The static
/// meshes are placements of the world media publication's meshes, so nothing here carries media; a later
/// assembly places these blocks for any location from its catalog record.
/// </summary>
public static class Arena2WorldBlocksPublication
{
    /// <summary>The importer identity the publication records.</summary>
    public const string ImporterId = "daggerfall-import/world-blocks";

    /// <summary>The block index's path in the publication.</summary>
    public const string IndexRelativePath = "blocks.json";

    /// <summary>
    /// Normalizes every placed block.
    /// </summary>
    /// <param name="sources">BLOCKS.BSA, ARCH3D.BSA, FACTION.TXT and every supplied texture leaf.</param>
    /// <param name="locations">The catalog's locations, which name every block a location places.</param>
    /// <param name="meshPublication">The world media publication's content root, whose meshes the models name.</param>
    /// <param name="progress">Told how far the publication has come, for a long run.</param>
    public static WorldBlocksPublication Create(
        DungeonLogicalSourceSet sources,
        DaggerfallLocations locations,
        string meshPublication,
        Action<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(locations);
        ArgumentException.ThrowIfNullOrWhiteSpace(meshPublication);
        WorldBlockNormalizer normalizer = new(sources);
        string[] rmb = [.. locations.Locations
            .SelectMany(location => location.Exterior?.Blocks ?? [])
            .Select(block => block.SourceName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        string[] rdb = [.. locations.Dungeons
            .SelectMany(dungeon => dungeon.Blocks)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        List<WorldBlockNormalization> normalized = [];
        int interiors = 0;
        foreach ((string sourceKey, int ordinal) in rmb.Select((key, ordinal) => (key, ordinal)))
        {
            RmbBlockContent content = normalizer.ReadRmb(sourceKey);
            normalized.Add(normalizer.RmbExterior(content));
            for (int building = 0; building < content.Summary.Buildings.Count; building++)
            {
                if (!content.HasInterior(building)) continue;
                normalized.Add(normalizer.RmbInterior(content, building));
                interiors++;
            }

            if ((ordinal + 1) % 100 == 0) progress?.Invoke($"{ordinal + 1} of {rmb.Length} RMB blocks, {interiors} interiors");
        }

        foreach (string sourceKey in rdb) normalized.Add(normalizer.Rdb(sourceKey));
        progress?.Invoke($"{rmb.Length} RMB blocks, {interiors} interiors, {rdb.Length} RDB blocks normalized");

        HashSet<string> placed = [.. rmb];
        string[] unplaced = [.. normalizer.Blocks.Records
            .Select(record => record.Name)
            .OfType<string>()
            .Where(name => name.EndsWith(".RMB", StringComparison.OrdinalIgnoreCase) && !placed.Contains(name))
            .Order(StringComparer.Ordinal)];
        List<ImportPublicationArtifact> artifacts = [];
        List<DaggerfallWorldBlockEntry> entries = [];
        foreach (WorldBlockNormalization block in normalized.OrderBy(value => value.Block.Key, StringComparer.Ordinal))
        {
            string document = $"{block.Block.Key}.json";
            artifacts.Add(new(document, JsonSerializer.SerializeToUtf8Bytes(block.Block, PublishedJson.SectionCompact),
                block.Spatial is null ? null : [block.Spatial.RelativePath]));
            if (block.Spatial is not null) artifacts.Add(new(block.Spatial.RelativePath, block.Spatial.Bytes.Span));
            entries.Add(new(block.Block.Key, block.Block.Kind, block.Block.SourceKey, block.Block.BuildingIndex, document, block.Spatial?.RelativePath));
        }

        PublishedSource[] published = [.. sources.Sources.Select(source => PublishedSource.Of(source.Label, source.Bytes.Span))];
        DaggerfallWorldBlockIndex index = new(
            meshPublication,
            new(rmb.Length, interiors, rdb.Length),
            entries,
            unplaced,
            published);
        artifacts.Add(new(IndexRelativePath, JsonSerializer.SerializeToUtf8Bytes(index, PublishedJson.SectionCompact),
            [.. entries.Select(entry => entry.Document).Order(StringComparer.Ordinal)]));
        ImportProvenance provenance = new(ImporterId, ImporterBuild.Revision,
            [.. sources.Sources.Select(source => new LogicalSourceRecord(source.Label, ContentDigest.Compute(source.Bytes.Span), source.Bytes.Length))]);
        string[] meshes = [.. normalized.SelectMany(block => block.Block.Models).Select(model => model.MeshArtifactId).OfType<string>()
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        return new(ImportPublicationPlan.Create(provenance, artifacts), index, meshes);
    }
}
