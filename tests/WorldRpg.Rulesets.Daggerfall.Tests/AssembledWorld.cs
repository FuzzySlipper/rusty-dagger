using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.Presentation;
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
        DaggerfallWorldMedia media = DaggerfallWorldMedia.Read(new DaggerfallData(full));
        Lazy<DaggerfallProductMedia> product = new(() => DaggerfallSiteContent.ReadProductMedia(new DaggerfallData(full), media, Definitions));
        Lazy<IReadOnlyDictionary<string, DaggerfallWorldMesh>> meshes = new(() => DaggerfallLocationAssembly.ReadMeshIndex(new DaggerfallData(full), media));
        WorldBlocks = new DaggerfallWorldBlocks(new DaggerfallData(full));
        DaggerfallLocationAssembly assembly = new(Definitions, WorldBlocks, () => product.Value, () => meshes.Value);
        Profiles = DaggerfallSiteProfiles.Resolve([DaggerfallAuthoredSite.Of(Source)], assembly, null);
        ResolvedGameComposition composition = GameCompositionResolver.Resolve(full, new GameBundleId("daggerfall.classic")).RequireComposition();
        Identity = composition.Identity;
        ContentPacks = [.. composition.ContentPacks];
        PublishedKeys = composition.ContentPacks.Where(pack => pack.Role == DaggerfallRuleset.SiteRole)
            .Select(pack => DaggerfallSiteContent.ReadHeader(pack.Payload, pack.Id.Value))
            .Where(header => header.VariantName is null).Select(header => header.Key).ToHashSet();
        Sky = DaggerfallSkyMedia.Read(new DaggerfallData(full));
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
    internal AssembledWorldRun Start(DaggerfallSiteId destination, DaggerfallSiteBuildingId? building = null, bool loadingScreen = false,
        IRandomService? random = null) =>
        LeaveHold(new(this, (RulesetSavePayload?)null, destination, building, loadingScreen, random));

    /// <summary>
    /// <see cref="Start(DaggerfallSiteId, DaggerfallSiteBuildingId?)"/> over the real Engine content, spatial, graphics and
    /// world origin services of <paramref name="engine"/>, whose host admits <paramref name="files"/> from
    /// <see cref="AssembledWorldRun.EngineContent"/>.
    /// </summary>
    internal AssembledWorldRun Start(IEngineContext engine, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files) =>
        LeaveHold(new(this, engine, files));

    /// <summary>A new game over the real Engine services of <paramref name="engine"/>, still in Privateer's Hold.</summary>
    internal AssembledWorldRun StartInHold(IEngineContext engine, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files) =>
        new(this, engine, files);

    private AssembledWorldRun LeaveHold(AssembledWorldRun run)
    {
        (DaggerfallSitePortal Portal, DurableIdentityReference _, EntityId Entity) exit = Assert.Single(run.Session.Sites.Projection.Portals.All);
        run.Use(exit.Portal, exit.Entity);
        run.FinishLoading();
        DaggerfallWorldProfileKey island = DaggerfallWorldProfileIds.Exterior(PrivateersHold);
        Assert.Equal(island, run.Session.Sites.ActiveProfile);
        DaggerfallSiteAnchor landing = Profiles.Require(island).RequireAnchor(DaggerfallLocationAssembly.DungeonEntranceAnchor);
        // The player stands on the ground at the landing: over it, and raised onto real collision when there is some.
        WorldPoint stood = run.Session.Sites.ExteriorSitePosition(run.Session.State.PlayerControl.Position!.Value);
        Assert.True(Vector2.Distance(new(landing.Position.X, landing.Position.Z), new(stood.X, stood.Z)) < 1e-2F, $"{landing.Position} vs {stood}");
        Assert.True(stood.Y > landing.Position.Y - 1e-2F, $"{landing.Position} vs {stood}");
        return run;
    }

    internal AssembledWorldRun Restore(RulesetSavePayload save, DaggerfallSiteId destination, DaggerfallSiteBuildingId? building = null) =>
        new(this, save, destination, building);

    /// <summary>A save restored over the real Engine services of <paramref name="engine"/>, as loading it in the running product does.</summary>
    internal AssembledWorldRun Restore(IEngineContext engine, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files, RulesetSavePayload save) =>
        new(this, engine, files, save);

    /// <summary>Every assembled exterior an exterior window centred on <paramref name="site"/> streams, a step across its map pixel's edge included.</summary>
    internal IEnumerable<DaggerfallSiteProfile> NearbyExteriors(DaggerfallSiteId site)
    {
        DaggerfallSiteRecord centre = Definitions.Locations.Records.Single(record => record.Id == site);
        foreach (DaggerfallSiteRecord near in Definitions.Locations.Records.Where(record => record.Exterior is not null
            && Math.Abs(record.Exterior.MapPixelX - centre.MapPixelX) <= 4 && Math.Abs(record.Exterior.MapPixelY - centre.MapPixelY) <= 4))
            if (Profiles.TryGet(DaggerfallWorldProfileIds.Exterior(near.Id), out DaggerfallSiteProfile exterior)) yield return exterior;
    }
}

/// <summary>One session over content admitting every place the journey and its window reach.</summary>
internal sealed class AssembledWorldRun : IDisposable
{
    private readonly PerceptionFake _perception = PerceptionFake.Create();
    private readonly AssembledWorld _world;
    private ulong _step;

    internal AssembledWorldRun(AssembledWorld world, RulesetSavePayload? save, DaggerfallSiteId destination, DaggerfallSiteBuildingId? building,
        bool loadingScreen = false, IRandomService? random = null)
    {
        _world = world;
        List<string> releases = [];
        ContentFake content = Content(world, destination, building, releases);
        Spatial = SpatialFake.Create(world.Source.SpatialArtifact.Sha256, releases);
        // The player stays where a test places them, so a door is used from where they stood.
        Spatial.KeepPosition = true;
        Appearance = new AppearanceFake(releases);
        EngineContextFake engine = EngineContextFake.Create(content, Spatial.Service,
            Appearance, _perception.Service, random: random ?? LodgingRandom.Create());
        // A run with a loading screen stages the player's slow world changes on it, as the host's does.
        Loading = loadingScreen ? new LoadingScreen(() => Session?.PublishLoading()) : null;
        Scripted = engine;
        DaggerfallSessionComposition composition = new(world.Definitions, world.Source, DaggerfallTuning.Defaults, world.Identity)
            { Profiles = world.Profiles, Sky = world.Sky, Blocks = world.Blocks, Loading = Loading };
        Session = save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
    }

    /// <summary>The scripted Engine context of a run over fakes: what the session published.</summary>
    internal EngineContextFake? Scripted { get; }

    /// <summary>The <c>loading</c> field of the session's last snapshot, decoded; null when it showed no loading screen.</summary>
    internal IReadOnlyDictionary<string, object?>? PublishedLoading() =>
        (IReadOnlyDictionary<string, object?>?)((IReadOnlyDictionary<string, object?>)Scripted!.Published()!)["loading"];

    /// <summary>The loading screen this run's session stages slow work on, or null when it runs that work at once.</summary>
    internal LoadingScreen? Loading { get; }

    /// <summary>Gives staged work the updates it runs on, as the host does, until the loading screen clears.</summary>
    internal void FinishLoading()
    {
        for (int update = 0; update < 8 && Loading is { IsLoading: true }; update++) Step();
        Assert.False(Loading?.IsLoading ?? false);
    }

    /// <summary>A new game over a real Engine's content, spatial, graphics and world origin; only perception stays scripted.</summary>
    internal AssembledWorldRun(AssembledWorld world, IEngineContext real, IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files,
        RulesetSavePayload? save = null)
    {
        _world = world;
        Graphics = new EngineGraphicsOutsideUpdate(real.Graphics);
        EngineContextFake engine = EngineContextFake.Create(real.Content, real.Spatial, Graphics, _perception.Service,
            random: LodgingRandom.Create(), worldOrigin: real.WorldOrigin);
        // The sky's bundle is served over the Engine's own content, so its graphics opens live references.
        ProductContent sky = new(new[] { new ProductContentFile(System.Text.Encoding.UTF8.GetBytes(DaggerfallSkyMedia.ManifestPath),
                files[DaggerfallSkyMedia.ManifestPath]) },
            EngineBundleContent.Create(real.Content, DaggerfallSkyMedia.BundleId, DaggerfallSkyMedia.LogicalRoot, files));
        DaggerfallSessionComposition composition = new(world.Definitions, world.Source, DaggerfallTuning.Defaults, world.Identity)
            { Profiles = world.Profiles, Sky = DaggerfallSkyMedia.Read(new DaggerfallData(sky)), Blocks = world.Blocks };
        Session = save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
        Engine = engine;
    }

    /// <summary>The scripted remainder of the Engine context a run over a real Engine draws through.</summary>
    internal EngineContextFake Engine { get; } = null!;

    /// <summary>
    /// The content files a run reaching <paramref name="destination"/> (and <paramref name="building"/>) opens, with the
    /// bytes a real Engine host is given for each: the checkout's file, or the bytes the test content already holds. The
    /// sky's bundle files are admitted under their content root.
    /// </summary>
    internal static Dictionary<string, ReadOnlyMemory<byte>> EngineContent(AssembledWorld world, DaggerfallSiteId destination,
        DaggerfallSiteBuildingId? building = null)
    {
        string root = Path.Combine(TestData.RepositoryRoot, "content");
        Dictionary<string, ReadOnlyMemory<byte>> files = new(StringComparer.Ordinal);
        foreach ((string path, byte[] bytes) in ContentPaths(world, destination, building)) files.Add(path, bytes);
        files[DaggerfallSkyMedia.ManifestPath] = File.ReadAllBytes(Path.Combine(root, DaggerfallSkyMedia.ManifestPath));
        foreach (string file in Directory.EnumerateFiles(Path.Combine(root, DaggerfallSkyMedia.LogicalRoot), "*", SearchOption.AllDirectories))
            files[Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/')] = File.ReadAllBytes(file);
        return files;
    }

    private static IEnumerable<(string Path, byte[] Bytes)> ContentPaths(AssembledWorld world, DaggerfallSiteId destination,
        DaggerfallSiteBuildingId? building)
    {
        ContentFake content = Content(world, destination, building, []);
        // The scripted graphics never opens the door, gate and action models a real Engine draws.
        foreach (DaggerfallSiteProfile profile in Profiles(world, destination, building))
        {
            foreach (DaggerfallRdbDoorDefinition door in profile.Doors)
                if (door.Visual is { } visual) content.Add(visual.Path, visual.Sha256);
            foreach (DaggerfallCityGateDefinition gate in profile.CityGates)
            {
                content.Add(gate.Open.Visual.Path, gate.Open.Visual.Sha256);
                content.Add(gate.Closed.Visual.Path, gate.Closed.Visual.Sha256);
            }
            foreach (DaggerfallDungeonActionModelDefinition model in profile.DungeonActionModels)
                content.Add(model.Visual.Path, model.Visual.Sha256);
        }
        foreach (DaggerfallMissileVisual visual in world.Source.ClassicPresentation.WorldVisuals)
        {
            content.Add(visual.Path, visual.Sha256);
            foreach (DaggerfallMissileTextureBinding texture in visual.Textures) content.Add(texture.TexturePath, texture.TextureSha256);
        }
        foreach (string path in content.Paths.Order(StringComparer.Ordinal))
            yield return (path, content.Body(path) ?? File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content", path)));
    }

    private static ContentFake Content(AssembledWorld world, DaggerfallSiteId destination, DaggerfallSiteBuildingId? building, List<string> releases)
    {
        ContentFake content = new(releases);
        PopulateContent(content, world.Source);
        foreach (DaggerfallSiteProfile profile in Profiles(world, destination, building)) Admit(content, profile);
        return content;
    }

    /// <summary>
    /// The profiles a run admits: each location's exterior and dungeon and every exterior its window streams, including
    /// the cells a step across its own map pixel's edge brings in, and the entered building's interior.
    /// </summary>
    private static IEnumerable<DaggerfallSiteProfile> Profiles(AssembledWorld world, DaggerfallSiteId destination, DaggerfallSiteBuildingId? building)
    {
        foreach (DaggerfallSiteId site in new[] { AssembledWorld.PrivateersHold, destination }.Distinct())
        {
            foreach (DaggerfallSiteProfile exterior in world.NearbyExteriors(site)) yield return exterior;
            if (world.Profiles.TryGet(DaggerfallWorldProfileIds.Dungeon(site), out DaggerfallSiteProfile dungeon)) yield return dungeon;
        }
        if (building is { } entered) yield return world.Profiles.Require(DaggerfallWorldProfileIds.Interior(destination, entered));
    }

    internal DaggerfallSession Session { get; }

    /// <summary>The Engine's graphics a run over a real Engine draws through, absent from a scripted run.</summary>
    internal EngineGraphicsOutsideUpdate Graphics { get; } = null!;

    /// <summary>The scripted spatial service, absent from a run over a real Engine.</summary>
    internal SpatialFake Spatial { get; } = null!;
    /// <summary>The scripted graphics of a run over fakes: what it published, overlay sets included.</summary>
    internal AppearanceFake Appearance { get; } = null!;

    /// <summary>One admitted update with no input.</summary>
    internal void Step()
    {
        // While work is staged the host gives the update to the loading screen, not the session.
        if (Loading?.Advance() == true) return;
        Session.Update(new ProductUpdate(OuterUpdate(++_step), []));
    }

    /// <summary>Changes the activation mode as the mode buttons do.</summary>
    internal void SetMode(string mode) => Submit(new { action = "activation-mode", mode });

    internal void ContinueGreeting()
    {
        var prompt = Session.DungeonTextProjection;
        Assert.NotNull(prompt);
        Submit(new { action = "dungeon-text-close", item = prompt.ActionId, revision = prompt.Revision.ToString(System.Globalization.CultureInfo.InvariantCulture) });
    }

    /// <summary>Advances the session's calendar to the next whole <paramref name="hour"/>.</summary>
    internal void AdvanceToHour(int hour)
    {
        DaggerfallCalendarSave now = DaggerfallSavePayload.Read(Session.CaptureSave()).Calendar;
        long seconds = ((((hour - now.Hour) % 24) + 24) % 24 * 3600L) - (now.Minute * 60L) - now.Second;
        if (seconds <= 0) seconds += 24 * 3600L;
        Session.AdvanceElapsedTime(seconds);
        Assert.Equal(hour, DaggerfallSavePayload.Read(Session.CaptureSave()).Calendar.Hour);
    }

    /// <summary>Walks forward for <paramref name="steps"/> admitted updates, holding the forward key and releasing it after.</summary>
    internal void Walk(int steps)
    {
        Session.Update(new ProductUpdate(OuterUpdate(++_step), [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)]));
        for (int step = 1; step < steps; step++) Step();
        Session.Update(new ProductUpdate(OuterUpdate(++_step), [Input(InputEventKind.Key, InputEdge.Released, keyboard: KeyboardControl.KeyW)]));
    }

    /// <summary>
    /// Walks to within <paramref name="within"/> of <paramref name="target"/> (live coordinates) with the forward key held,
    /// turning towards it on every update as a player steers with the mouse. Collision, ground and slopes are the Engine's.
    /// Returns whether the player got there within <paramref name="maximumSteps"/> admitted updates.
    /// </summary>
    internal bool WalkTo(Vector3 target, float within, int maximumSteps)
    {
        bool arrived = false;
        for (int step = 0; step < maximumSteps; step++)
        {
            Vector3 here = Session.State.PlayerControl.Position!.Value.ToVector();
            Vector2 offset = new(target.X - here.X, target.Z - here.Z);
            if (offset.Length() <= within)
            {
                arrived = true;
                break;
            }
            Session.State.PlayerControl.YawRadians = WorldRpg.Kit.Actors.ActorHeading.Yaw(new Vector3(offset.X, 0F, offset.Y));
            Session.Update(new ProductUpdate(OuterUpdate(++_step), step == 0
                ? [Input(InputEventKind.Key, InputEdge.Pressed, keyboard: KeyboardControl.KeyW)] : []));
        }
        Session.Update(new ProductUpdate(OuterUpdate(++_step), [Input(InputEventKind.Key, InputEdge.Released, keyboard: KeyboardControl.KeyW)]));
        return arrived;
    }

    /// <summary>
    /// Walks to <paramref name="target"/> as <see cref="WalkTo"/> does, and when a wall stops the player short, looks along
    /// it with the Engine's rays for the nearest opening at least two metres wide, walks through it and carries on, as a
    /// player finding the gate in a wall would. Returns whether the player got there within <paramref name="detours"/> openings.
    /// </summary>
    internal bool WalkAround(Vector3 target, float within, int detours = 4)
    {
        for (int detour = 0; ; detour++)
        {
            if (WalkTo(target, within, 60 * 120)) return true;
            if (detour == detours) return false;
            Vector3 here = Session.State.PlayerControl.Position!.Value.ToVector();
            Vector3 ahead = Vector3.Normalize(new Vector3(target.X - here.X, 0F, target.Z - here.Z));
            // Scan along the wall, not perpendicular to an oblique destination bearing: the latter
            // can put a probe behind the wall and mistake that clear ray for an opening.
            SpatialHit wall = Session.Spatial.CastRay(here, ahead, 3F);
            Vector3 normal = new(wall.Normal.X, 0F, wall.Normal.Z);
            if (wall.Present && normal.LengthSquared() > .5F) ahead = -Vector3.Normalize(normal);
            Vector3 along = new(-ahead.Z, 0F, ahead.X);
            float? opening = null;
            for (int offset = 1; offset <= 80 && opening is null; offset++)
                foreach (int side in new[] { 1, -1 })
                    if (Clear(here + (along * (side * offset)), ahead) && Clear(here + (along * (side * (offset + 1))), ahead)
                        && Clear(here + (along * (side * (offset + 2))), ahead))
                    {
                        opening = side * (offset + 1);
                        break;
                    }
            if (opening is not float sideways) return false;
            Vector3 gate = here + (along * sideways);
            if (!WalkTo(gate, .3F, 60 * 60) || !WalkTo(gate + (ahead * 4F), .3F, 60 * 30)) return false;
        }

        bool Clear(Vector3 from, Vector3 direction) => !Session.Spatial.CastRay(from, direction, 3F).Present;
    }

    /// <summary>
    /// Uses a portal from wherever the player stands, turning to face its door but not moving: a use from out of reach
    /// is refused as it is in play. Returns where the player stood (in the active frame's profile coordinates when outside).
    /// </summary>
    internal WorldPoint UseFromWhereStanding(DaggerfallSitePortal portal, EntityId entity)
    {
        Vector3 eye = Session.State.PlayerControl.Position!.Value.ToVector();
        Vector3 toDoor = portal.Position.ToVector() - eye;
        Session.State.PlayerControl.YawRadians = WorldRpg.Kit.Actors.ActorHeading.Yaw(toDoor with { Y = 0F });
        Session.State.PlayerControl.PitchRadians = 0F;
        return UseFromHere(entity);
    }

    /// <summary>Pays for and makes the journey through the travel window, arriving.</summary>
    internal void Travel(DaggerfallSiteRecord destination)
    {
        AcceptJourney(destination);
        FinishLoading();
        Assert.Equal(DaggerfallTravelOutcome.Arrived, Session.State.Travel.LastResult!.Outcome);
        // The purse's leftover coins weigh far more than the traveller can carry; taken back, they walk on arrival.
        ulong left = Session.State.Inventory.Read().Stacks.Single(value => value.Id == JourneyGold).Quantity;
        Session.State.Inventory.Consume(new(JourneyGold, left));
        Session.State.ItemInstances.RemoveStack(DaggerfallItemOwner.Player, JourneyGold);
    }

    private static readonly InventoryStackId JourneyGold = InventoryStackId.Parse("journey.gold");

    /// <summary>Discovers the destination, carries the fare, and accepts the journey's quote in the travel window.</summary>
    internal void AcceptJourney(DaggerfallSiteRecord destination)
    {
        Session.Site.Discover(destination.Id);
        DaggerfallItemDefinition gold = _world.Definitions.RequireItem(new DaggerfallItemId("gold-piece"));
        Session.State.Inventory.Grant(new(new InventoryItemId(gold.Id.Value), JourneyGold, 100_000));
        Session.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Player, JourneyGold, DaggerfallItemInstanceMetadata.Default(gold, DaggerfallItemOwner.Player));
        Submit(new { action = "travel-preview", region = destination.Region, destination = destination.Index, cautious = false, inn = true, ship = false });
        DaggerfallTravelQuote quote = Session.ReadTravelPresentation().Quote ?? throw new InvalidOperationException(Session.ReadTravelPresentation().Message);
        Assert.Null(Session.ReadTravelPresentation().Message);
        Submit(new { action = "travel-accept", key = quote.Identity, amount = quote.TotalCost });
    }

    /// <summary>Every observer sees every target it asks about, as creatures beside the player in open ground do.</summary>
    internal void SeeEverything() =>
        _perception.Responder = request => Receipt([.. request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray()
            .Select(target => new PerceptionPair(observer.Entity, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d)))]);

    /// <summary>Uses the target at a live position, returning where the player stood (in the active frame's profile coordinates when outside).</summary>
    internal WorldPoint Use(Vector3 target, EntityId entity)
    {
        AimActivationAt(Session, WorldPoint.From(target));
        return UseFromHere(entity);
    }

    /// <summary>Uses a portal from a metre in front of its door, on the side it is used from, facing it.</summary>
    internal WorldPoint Use(DaggerfallSitePortal portal, EntityId entity)
    {
        Vector3 normal = Assert.NotNull(portal.Normal);
        Session.State.PlayerControl.MoveTo(portal.Position.ToVector() + normal);
        Session.State.PlayerControl.YawRadians = WorldRpg.Kit.Actors.ActorHeading.Yaw(-normal);
        Session.State.PlayerControl.PitchRadians = 0F;
        return UseFromHere(entity);
    }

    private WorldPoint UseFromHere(EntityId entity)
    {
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

    /// <summary>Admits a profile's content, terrain textures included, to a scripted content service.</summary>
    internal static void Admit(ContentFake content, DaggerfallSiteProfile profile) => PopulateContent(content, profile);
}
