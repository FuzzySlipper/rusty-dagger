using Rusty.Engine.Entities;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The Actors civilian owners' inputs without the session: an NPC registry with its allocator, the Kit actors with the
/// player, an exterior profile publishing one billboard and its appearance, and the roster's materialize (the actor factory's civilian), unload and retire recorded.
/// The profile is of <paramref name="site"/> (the first exterior when null) and places <paramref name="population"/>.
/// </summary>
internal sealed class CivilianStage : IDisposable
{
    private readonly IReadOnlyList<DaggerfallPopulationPlacement> _population;

    internal CivilianStage(DaggerfallSiteRecord? site = null, IReadOnlyList<DaggerfallPopulationPlacement>? population = null)
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        Site = site ?? definitions.Locations.Records.First(site => site.Exterior is not null);
        _population = population ?? [];
        Profile = At(DaggerfallWorldProfileKind.Exterior, "civilians");
        Look = new DaggerfallNpcAppearance("breton", "Male", 210, 4, 7, 0);
        ContentFake content = TestSessions.MediaContent(_releases);
        content.Add("sprite/population.png", TestSessions.Hash);
        Appearance = new DaggerfallSiteAppearance(content, new AppearanceFake(_releases), Profile);
        Npcs.Identities = new DurableIdentityAllocator(DurableIdentityKind.Actor, 100);
        Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), TestSessions.HealthyMechanics(), "health", DaggerfallActorFactory.PlayerCapabilities);
        Civilians = new DaggerfallCivilians(Npcs, Actors, () => Profile, () => Appearance, id => id == Site.Id ? Site : throw new KeyNotFoundException(),
            point => WorldPoint.From(point.ToVector() + Offset), Materialize, id => Unloaded.Add(id), id => Retired.Add(id), id => Retained.Remove(id));
    }

    /// <summary>The media fixture's content as a profile of this site, publishing one person's billboard (210/4).</summary>
    internal DaggerfallSiteProfile At(DaggerfallWorldProfileKind kind, string logicalId)
    {
        DaggerfallSiteProfile source = TestSessions.MediaInputs();
        NormalizedBillboardSprite billboard = new("sprite/population.png", TestSessions.Hash, 8, 8,
            [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new System.Numerics.Vector2(.5F, .5F), System.Numerics.Vector2.One);
        return new DaggerfallSiteProfile(new ProjectFacts(new WorldPoint(1f, 1f, 1f), new Dictionary<long, AuthoredActor>()), source.Geometry,
            source.WorldAppearance, source.InitialLook, source.Materials, source.ActorSprites, site: Site.Id, profileKind: kind,
            logicalProfileId: logicalId, billboardSprites: new Dictionary<(int Archive, int Record), NormalizedBillboardSprite> { [(210, 4)] = billboard }, population: _population);
    }

    /// <summary>The profile frame's offset from the scene's local frame.</summary>
    internal static readonly System.Numerics.Vector3 Offset = new(100f, 0f, 0f);
    internal DaggerfallSiteRecord Site { get; }
    internal DaggerfallSiteProfile Profile { get; }
    private readonly DaggerfallMechanicsState _mechanics = new();
    private readonly Rusty.Engine.Mechanics.InventoryStore _inventories = new();
    internal DaggerfallNpcRegistry Npcs { get; } = new();
    internal ActorsState Actors { get; } = new();
    internal List<(long Id, ActorPose Pose)> Materialized { get; } = [];
    internal List<long> Unloaded { get; } = [];
    internal List<long> Retired { get; } = [];
    internal HashSet<long> Retained { get; } = [];
    internal DaggerfallCivilians Civilians { get; }
    internal DaggerfallNpcSite NpcSite => new(Site.Id.Region, Site.Name, string.Empty);
    internal DaggerfallNpcAppearance Look { get; }
    internal DaggerfallSiteAppearance Appearance { get; }
    private readonly List<string> _releases = [];

    internal long Register(bool sourcePopulation = false) => sourcePopulation
        ? Npcs.RegisterPopulationCivilian(NpcSite, $"population/{Npcs.All.Count}", Look, "civilian", [])
        : Npcs.RegisterCivilian(NpcSite, Look, "civilian", []);

    private long Materialize(DaggerfallNpc npc, ActorPose pose)
    {
        DaggerfallActorFactory.CreateCivilianActor(_mechanics, Actors, _inventories, npc, pose, TestPayload.Definitions, out _);
        Materialized.Add((npc.DurableId, pose));
        return npc.DurableId;
    }

    public void Dispose()
    {
        Appearance.Dispose();
        Actors.Dispose();
    }
}
