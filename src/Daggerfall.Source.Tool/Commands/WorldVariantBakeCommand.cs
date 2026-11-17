using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;

namespace Daggerfall.Source.Tool.Commands;

/// <summary>
/// Bakes a content pack's world variants: each block-variant recipe in its payload's <c>worldVariants.blocks</c> is
/// normalized over the player's Arena2 files as the import normalizes the base block (<see cref="WorldBlockNormalizer.RmbExteriorVariant"/>),
/// and its block document and collision/navigation artifact are written below the pack's <c>variants/</c> folder at the
/// paths the document names, for the pack to commit. The folder is the bake's alone: a file no recipe produces is removed.
/// </summary>
/// <remarks>
/// Each document is stamped with the digest of the base block's document (<see cref="DaggerfallWorldBlock.BaseDigest"/>),
/// so a game whose import has since changed the base block warns that the variant was drawn over another block: bake
/// again then, and after editing a recipe.
/// </remarks>
internal static class WorldVariantBakeCommand
{
    /// <summary>The folder beside a pack's payload that holds the baked variants.</summary>
    internal const string Folder = "variants";

    public static ToolCommand Command { get; } = new("world-variant-bake",
    [
        Options.Arena2,
        CommandOption.Required("--pack", "PACK_DIR"),
    ], Run, Program.Tool);

    private static int Run(CommandArguments args)
    {
        string arena2 = Path.GetFullPath(args[Options.Arena2.Name]);
        string pack = Path.GetFullPath(args["--pack"]);
        string payload = Path.Combine(pack, "payload.json");
        IReadOnlyList<WorldBlockVariantRecipe> recipes = WorldBlockVariantRecipe.ReadPayload(File.ReadAllBytes(payload), payload);
        DungeonLogicalSourceSet sources = new([
            .. new[] { "BLOCKS.BSA", "ARCH3D.BSA", "FACTION.TXT", "FLATS.CFG" }.Select(name => Source(arena2, name)),
            .. Directory.EnumerateFiles(arena2, "TEXTURE.*").Select(path => Source(arena2, Path.GetFileName(path))),
        ]);
        WorldBlockNormalizer normalizer = new(sources);
        string folder = Path.Combine(pack, Folder);
        HashSet<string> written = [];
        foreach (WorldBlockVariantRecipe recipe in recipes)
        {
            WorldBlockNormalization variant = normalizer.RmbExteriorVariant(recipe);
            written.Add(Write(folder, $"{variant.Block.Key}.json", Arena2WorldBlocksPublication.DocumentBytes(variant.Block)));
            if (variant.Spatial is { } spatial) written.Add(Write(folder, spatial.RelativePath, spatial.Bytes.Span));
            Console.WriteLine($"world-variant-bake: {recipe.Block} '{recipe.Variant}': {variant.Block.Models.Count} models, base {variant.Block.BaseDigest!.Value.Value[..12]}");
        }

        if (Directory.Exists(folder))
            foreach (string stale in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Where(path => !written.Contains(path)))
            {
                File.Delete(stale);
                Console.WriteLine($"world-variant-bake: removed {Path.GetRelativePath(pack, stale)}, which no recipe produces");
            }

        return 0;
    }

    private static string Write(string folder, string relativePath, ReadOnlySpan<byte> bytes)
    {
        string path = Path.GetFullPath(Path.Combine(folder, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes.ToArray());
        return path;
    }

    private static DungeonLogicalSource Source(string arena2, string name) => new(Options.Arena2Label(name), File.ReadAllBytes(Path.Combine(arena2, name)));
}
