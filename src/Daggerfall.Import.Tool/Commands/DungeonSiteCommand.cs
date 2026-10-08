using Daggerfall.Import.Normalization;
using Daggerfall.Import.Normalized;
using Daggerfall.Import.Publication;
using WorldRpg.SpriteAuthoring;

namespace Daggerfall.Import.Tool.Commands;

/// <summary>
/// Publishes one RDB dungeon site closure (<c>write</c>), compares the closure it would write with the one on
/// disk (<c>plan</c>), or builds it twice and checks both builds are identical (<c>verify-real-data</c>).
/// </summary>
internal static class DungeonSiteCommand
{
    private static readonly CommandOption SpriteAuthoring = CommandOption.Optional("--sprite-authoring", "SOURCE_DIR");
    private static readonly CommandOption SpriteOverlay = CommandOption.Optional("--sprite-overlay", "sprites/RELATIVE.json");

    private static IReadOnlyList<CommandOption> SiteOptions => [.. SiteInputs.Common, SpriteAuthoring, SpriteOverlay];

    public static ToolCommand Write { get; } = new("write", SiteOptions, args =>
    {
        ImportPublicationPlan plan = SiteInputs.Reference(args, BuildPlan(args));
        SiteInputs.PrintComparison(ImportPublicationWriter.Write(plan, Path.GetFullPath(args[SiteInputs.Output.Name])));
        SiteInputs.WriteSourceManifest(args, plan);
        return 0;
    });

    public static ToolCommand Plan { get; } = new("plan", SiteOptions, args =>
    {
        SiteInputs.PrintComparison(SiteInputs.Reference(args, BuildPlan(args)).Compare(Path.GetFullPath(args[SiteInputs.Output.Name])));
        return 0;
    });

    public static ToolCommand VerifyRealData { get; } = new("verify-real-data", SiteOptions, args =>
    {
        string root = Path.Combine(Path.GetTempPath(), $"daggerfall-import-verify-{Guid.NewGuid():N}");
        try
        {
            ImportPublicationWriter.Write(SiteInputs.Reference(args, BuildPlan(args)), Path.Combine(root, "first"));
            ImportPublicationWriter.Write(SiteInputs.Reference(args, BuildPlan(args)), Path.Combine(root, "second"));
            IReadOnlyDictionary<string, ContentDigest> first = HashClosure(Path.Combine(root, "first"));
            IReadOnlyDictionary<string, ContentDigest> second = HashClosure(Path.Combine(root, "second"));
            if (first.Count != second.Count || first.Any(entry => !second.TryGetValue(entry.Key, out ContentDigest hash) || hash != entry.Value))
            {
                throw new InvalidOperationException("Repeated real-data import did not produce the same publication closure.");
            }

            Console.WriteLine($"verified deterministic publication ({first.Count} artifacts)");
            return 0;
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    });

    private static ImportPublicationPlan BuildPlan(CommandArguments args)
    {
        int region = SiteInputs.ParseRegion(args);
        string location = SiteInputs.ParseLocation(args);
        Arena2ClassicMediaProfile classicMedia = AuthoredUi.Profile(args, required: true);
        string arena2 = Path.GetFullPath(args[Options.Arena2.Name]);
        ImportPublicationPlan Build(IReadOnlyList<AuthoredMediaOverlay> dungeonOverlays, IReadOnlyList<AuthoredMediaOverlay> classicOverlays) =>
            Arena2SitePublication.Dungeon(Arena2SiteSources.ForSite(arena2), region, location,
                SiteInputs.Media(args, classicMedia with { AuthoredOverlays = classicOverlays }, dungeonOverlays));

        if (args.Has(SpriteAuthoring.Name) != args.Has(SpriteOverlay.Name))
        {
            throw args.Invalid("--sprite-authoring and --sprite-overlay must be supplied together.");
        }

        if (!args.Has(SpriteAuthoring.Name)) return Build([], []).WithInvocation(SiteInputs.Invocation(args));

        // An overlay is validated against the closure as it stands without it, then split between the
        // dungeon sprites and the classic sprites it names. Its bytes are read once, so the document applied
        // and the digest recorded are the same.
        string authoring = Path.GetFullPath(args[SpriteAuthoring.Name]);
        string overlayPath = args[SpriteOverlay.Name];
        SpriteAuthoredOverlayStore.ValidateOverlayRelativePath(overlayPath);
        SpriteAuthoredOverlayStore.ValidateRootSeparation(Path.GetFullPath(args[SiteInputs.Output.Name]), authoring);
        SpritePublicationSnapshot current = SpritePublicationReader.FromPlan(Build([], []));
        byte[] overlayBytes = SpriteOverlays.ReadBytes(authoring, overlayPath);
        IReadOnlyList<AuthoredMediaOverlay> overlays = SpriteAuthoredMediaOverlays.ToMediaOverlays(
            SpriteAuthoredOverlayStore.Read(overlayBytes), current.Catalog, current.AuthoringBasisDigest);
        HashSet<string> dungeonIds = current.Catalog.Entries
            .Where(entry => entry.Kind is SpriteInspectionKind.DungeonBillboard or SpriteInspectionKind.DungeonActor or SpriteInspectionKind.DungeonCorpse)
            .Select(entry => entry.Id)
            .ToHashSet(StringComparer.Ordinal);
        return Build([.. overlays.Where(overlay => dungeonIds.Contains(overlay.Id))], [.. overlays.Where(overlay => !dungeonIds.Contains(overlay.Id))])
            .WithInvocation(SiteInputs.Invocation(args, [new PublishedSource(overlayPath, ContentDigest.Compute(overlayBytes), overlayBytes.LongLength)]));
    }

    private static IReadOnlyDictionary<string, ContentDigest> HashClosure(string directory) => Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal)
        .ToDictionary(
            path => Path.GetRelativePath(directory, path).Replace(Path.DirectorySeparatorChar, '/'),
            path => ContentDigest.Compute(File.ReadAllBytes(path)),
            StringComparer.Ordinal);
}
