using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The composed catalog over the shipped content: the authored packs by their ids and every other place assembled.
/// Sessions over it start a new game in Privateer's Hold and reach the rest of the world from there.
/// </summary>
internal sealed class AssembledWorld
{
    internal static readonly DaggerfallSiteId PrivateersHold = new(17, 179);

    internal DaggerfallDefinitions Definitions { get; }
    internal DaggerfallSiteProfile Source { get; }
    internal DaggerfallSiteProfiles Profiles { get; }
    internal ResolvedCompositionIdentity Identity { get; }
    internal DaggerfallSkyMedia Sky { get; }
    /// <summary>The block facts quest Places select buildings, dungeons and markers from, as the product admits them.</summary>
    internal DaggerfallBlocksSnapshot Blocks { get; }
    /// <summary>The per-block publication every assembled profile is built from.</summary>
    internal DaggerfallWorldBlocks WorldBlocks { get; }
    /// <summary>The profiles the shipped bundle's site packs publish; this world reads only Privateer's Hold's closure and assembles the rest.</summary>
    internal IReadOnlySet<DaggerfallWorldProfileKey> PublishedKeys { get; }
    /// <summary>The shipped bundle's content packs, the quest corpora among them.</summary>
    internal IReadOnlyList<ContentPack> ContentPacks { get; }

    /// <summary>The world over the shipped definitions, or over definitions a test has added its own quest sources to.</summary>
    internal AssembledWorld(DaggerfallDefinitions? definitions = null)
    {
        Definitions = definitions ?? TestPayload.Definitions;
        string root = TestData.RepositoryRoot;
        Source = ReadInputs(root);
        ProductContent full = FullContent(root);
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(full);
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(full, media, Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(full, media));
        WorldBlocks = new DaggerfallWorldBlocks(full);
        DaggerfallLocationAssembly assembly = new(Definitions, WorldBlocks, () => product.Value, () => meshes.Value);
        Profiles = DaggerfallSiteProfiles.Resolve([DaggerfallAuthoredSite.Of(Source)], assembly, null);
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(full, new GameBundleId("daggerfall.classic")).RequireComposition();
        Identity = composition.Identity;
        ContentPacks = [.. composition.ContentPacks];
        PublishedKeys = composition.ContentPacks.Where(pack => pack.Role == DaggerfallRuleset.SiteRole)
            .Select(pack => DaggerfallSiteContent.ReadHeader(pack.Payload, pack.Id.Value))
            .Where(header => header.VariantName is null).Select(header => header.Key).ToHashSet();
        Sky = DaggerfallSkyMedia.Read(full);
        Blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.blocks.json")));
    }

    /// <summary>Whether no site pack of the shipped bundle publishes any profile of this location.</summary>
    internal bool IsUnpublished(DaggerfallSiteId site) => !PublishedKeys.Any(key => key.Site == site);

    /// <summary>The location of Privateer's Hold's region nearest it that no pack publishes and the catalog places.</summary>
    internal DaggerfallSiteRecord Unpublished(Func<DaggerfallSiteRecord, bool> wanted)
    {
        DaggerfallSiteRecord origin = Definitions.Locations.Records.Single(record => record.Id == PrivateersHold);
        return Definitions.Locations.Records
            .Where(record => record.Region == PrivateersHold.Region && record.Id != PrivateersHold && record.Climate is not null)
            .Where(record => IsUnpublished(record.Id))
            .OrderBy(record => Math.Abs(record.MapPixelX - origin.MapPixelX) + Math.Abs(record.MapPixelY - origin.MapPixelY))
            .First(record => Profiles.Contains(DaggerfallWorldProfileIds.Exterior(record.Id)) && wanted(record));
    }

    /// <summary>
    /// A new game in Privateer's Hold, walked out through its exit onto its assembled exterior: with no
    /// entrance to return through, the player lands in front of the island's dungeon entrance.
    /// </summary>
    internal AssembledWorldRun Start(DaggerfallSiteId destination, DaggerfallSiteBuildingId? building = null)
    {
        AssembledWorldRun run = new(this, null, destination, building);
        (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) exit = Assert.Single(run.Session.Sites.Projection.Portals.All);
        run.Use(exit.Portal.Position.ToVector(), exit.Entity);
        DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(PrivateersHold);
        Assert.Equal(island, run.Session.Sites.ActiveProfile);
        DaggerfallSiteAnchor landing = Profiles.Require(island).RequireAnchor(DaggerfallLocationAssembly.DungeonEntranceAnchor);
        Assert.True(Vector3.Distance(landing.Position.ToVector(),
            run.Session.Sites.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value).ToVector()) < 1e-2F);
        return run;
    }

    internal AssembledWorldRun Restore(RulesetSavePayload save, DaggerfallSiteId destination, DaggerfallSiteBuildingId? building = null) =>
        new(this, save, destination, building);
}

/// <summary>One session over content admitting every place the journey and its window reach.</summary>
internal sealed class AssembledWorldRun : IDisposable
{
    private readonly PerceptionFake _perception = PerceptionFake.Create();
    private readonly AssembledWorld _world;
    private ulong _step;

    internal AssembledWorldRun(AssembledWorld world, RulesetSavePayload? save, DaggerfallSiteId destination, DaggerfallSiteBuildingId? building)
    {
        _world = world;
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, world.Source);
        foreach (DaggerfallSiteId site in new[] { AssembledWorld.PrivateersHold, destination }.Distinct())
            Populate(content, world, site);
        if (building is { } entered) Admit(content, world.Profiles.Require(DaggerfallWorldProfileIds.Interior(destination, entered)));
        Spatial = SpatialFake.Create(world.Source.SpatialArtifact.Sha256, releases);
        // The player stays where a test places them, so a door is used from where they stood.
        Spatial.KeepPosition = true;
        EngineContextFake engine = EngineContextFake.Create(content, Spatial.Service,
            new AppearanceFake(releases), _perception.Service, random: LodgingRandom.Create());
        DaggerfallSessionComposition composition = new(world.Definitions, world.Source, DaggerfallTuning.Defaults, world.Identity)
            { Profiles = world.Profiles, Sky = world.Sky, Blocks = world.Blocks };
        Session = save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
    }

    internal DaggerfallSession Session { get; }

    internal SpatialFake Spatial { get; }

    /// <summary>One admitted update with no input.</summary>
    internal void Step() => Session.Update(new ProductUpdate(OuterUpdate(++_step), []));

    /// <summary>Pays for and makes the journey through the travel window, arriving.</summary>
    internal void Travel(DaggerfallSiteRecord destination)
    {
        Session.Site.Discover(destination.Id);
        DaggerfallItemDefinition gold = _world.Definitions.RequireItem(new DaggerfallItemId("gold-piece"));
        InventoryStackId stack = InventoryStackId.Parse("journey.gold");
        Session.State.Inventory.Grant(new(new InventoryItemId(gold.Id.Value), stack, 100_000));
        Session.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Player, stack, DaggerfallItemInstanceMetadata.Default(gold, DaggerfallItemOwner.Player));
        Submit(new { action = "travel-preview", region = destination.Region, destination = destination.Index, cautious = false, inn = true, ship = false });
        DaggerfallTravelQuote quote = Session.ReadTravelPresentation().Quote ?? throw new InvalidOperationException(Session.ReadTravelPresentation().Message);
        Assert.Null(Session.ReadTravelPresentation().Message);
        Submit(new { action = "travel-accept", key = quote.Identity, amount = quote.TotalCost });
        Assert.Equal(DaggerfallTravelOutcome.Arrived, Session.State.Travel.LastResult!.Outcome);
    }

    /// <summary>Uses the target at a live position, returning where the player stood (in the active frame's profile coordinates when outside).</summary>
    internal WorldPoint Use(Vector3 target, EntityId entity)
    {
        AimActivationAt(Session, WorldPoint.From(target));
        WorldPoint stood = Session.Sites.ActiveProfile.Kind == DaggerfallWorldProfileKind.Exterior
            ? Session.Sites.ExteriorSitePosition(Session.State.PlayerControl.Position!.Value)
            : Session.State.PlayerControl.Position!.Value;
        _perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(candidate => new PerceptionPair(1, candidate.Entity, 1d, 1d,
            candidate.Entity == entity.Value ? PerceptionPairKind.Visible : PerceptionPairKind.Occluded, 1d))]);
        Submit(new { action = "loot" });
        return stood;
    }

    private void Submit(object action) => Session.Update(new ProductUpdate(OuterUpdate(++_step), [Ui(JsonSerializer.Serialize(action))]));

    public void Dispose() => Session.Dispose();

    /// <summary>
    /// Admits a location's exterior and dungeon and every exterior its window streams, including the cells a
    /// step across its own map pixel's edge brings in.
    /// </summary>
    private static void Populate(ContentFake content, AssembledWorld world, DaggerfallSiteId site)
    {
        DaggerfallSiteRecord centre = world.Definitions.Locations.Records.Single(record => record.Id == site);
        foreach (DaggerfallSiteRecord near in world.Definitions.Locations.Records.Where(record => record.Exterior is not null
            && Math.Abs(record.Exterior.MapPixelX - centre.MapPixelX) <= 4 && Math.Abs(record.Exterior.MapPixelY - centre.MapPixelY) <= 4))
            if (world.Profiles.TryGet(DaggerfallWorldProfileIds.Exterior(near.Id), out DaggerfallSiteProfile exterior)) Admit(content, exterior);
        if (world.Profiles.TryGet(DaggerfallWorldProfileIds.Dungeon(site), out DaggerfallSiteProfile dungeon)) Admit(content, dungeon);
    }

    private static void Admit(ContentFake content, DaggerfallSiteProfile profile)
    {
        PopulateContent(content, profile);
        foreach (NormalizedTerrainTexture texture in profile.TerrainTextures.Values) content.Add(texture.TexturePath, texture.TextureSha256);
    }
}
