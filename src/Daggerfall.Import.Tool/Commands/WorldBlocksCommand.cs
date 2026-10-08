using System.Diagnostics;
using System.Text.Json;
using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes the normalized per-block facts of every block a location places: each RMB exterior, each
/// building interior it declares and each RDB block, with its collision and navigation, in one process.
/// </summary>
/// <remarks>
/// It reads the catalog's locations for the blocks the world places, so it runs after the locations command,
/// and the world media publication its models' meshes are placements of, so it runs after world-media. Every
/// mesh a model names must be one that publication carries.
/// </remarks>
internal static class WorldBlocksCommand
{
    public static ToolCommand Command { get; } = new("world-blocks",
        [Options.Arena2, Options.Pack, SiteInputs.Shared, SiteInputs.Output, Options.Inventory, SiteInputs.SourceManifest], Run);

    private static int Run(CommandArguments args)
    {
        Stopwatch clock = Stopwatch.StartNew();
        string arena2 = Path.GetFullPath(args[Options.Arena2.Name]);
        DungeonLogicalSourceSet sources = new([
            .. new[] { "BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT" }.Select(name => Source(arena2, name)),
            .. Directory.EnumerateFiles(arena2, "TEXTURE.*").Select(path => Source(arena2, Path.GetFileName(path))),
        ]);
        DaggerfallLocations locations = PayloadFiles.ReadSection<DaggerfallLocations>(args[Options.Pack.Name], "locations");
        WorldBlocksPublication publication = Arena2WorldBlocksPublication.Create(sources, locations, Arena2SitePublication.WorldMediaImporterId,
            line => Console.WriteLine($"world blocks: {line} ({clock.Elapsed.TotalSeconds:F0}s)"));
        RequireSharedMeshes(publication, args[SiteInputs.Shared.Name]);
        ImportPublicationPlan plan = publication.Plan.WithInvocation(SiteInputs.Invocation(args));
        SiteInputs.PrintComparison(ImportPublicationWriter.Write(plan, Path.GetFullPath(args[SiteInputs.Output.Name])));
        SiteInputs.WriteSourceManifest(args, plan);
        DaggerfallWorldBlockCounts counts = publication.Index.Counts;
        long bytes = plan.Artifacts.Sum(artifact => (long)artifact.Bytes.Length);
        long spatial = plan.Artifacts.Where(artifact => artifact.RelativePath.EndsWith(SpatialArtifactBinary.Extension, StringComparison.Ordinal)).Sum(artifact => (long)artifact.Bytes.Length);
        Console.WriteLine($"world blocks: {counts.RmbExterior} RMB exteriors, {counts.RmbInterior} RMB interiors, {counts.Rdb} RDB blocks; {publication.Index.UnplacedRmbBlocks.Count} RMB records no location places");
        Console.WriteLine($"  {plan.Artifacts.Count} artifacts, {bytes} bytes ({spatial} in collision/navigation), {clock.Elapsed.TotalSeconds:F0}s");
        return 0;
    }

    private static DungeonLogicalSource Source(string arena2, string name) => new(Options.Arena2Label(name), File.ReadAllBytes(Path.Combine(arena2, name)));

    /// <summary>
    /// Refuses a model naming a mesh the world media publication does not carry: the block would place
    /// geometry nobody published.
    /// </summary>
    private static void RequireSharedMeshes(WorldBlocksPublication publication, string shared)
    {
        string index = Path.Combine(Path.GetFullPath(shared), GeometryPublication.IndexRelativePath);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(index));
        HashSet<string> published = [.. document.RootElement.GetProperty("meshes").EnumerateArray().Select(mesh => mesh.GetProperty("artifactId").GetString()!)];
        string[] missing = [.. publication.MeshArtifactIds.Where(mesh => !published.Contains(mesh))];
        if (missing.Length != 0)
            throw new InvalidOperationException($"Placed blocks name {missing.Length} meshes the world media publication at '{shared}' does not carry, for example {string.Join(", ", missing.Take(5))}: run world-media first.");
        Console.WriteLine($"world blocks: its {publication.MeshArtifactIds.Count} placed meshes are all among the {published.Count} the world media publication carries");
    }
}
