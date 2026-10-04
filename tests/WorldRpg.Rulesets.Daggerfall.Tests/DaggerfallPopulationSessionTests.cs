using System.Numerics;
using Daggerfall.Import.Normalized;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallPopulationSessionTests
{
    [Fact]
    public void Source_civilian_keeps_identity_corpse_and_pose_through_unload_and_save_restore()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord[] towns = [.. definitions.Locations.Records.Where(record => record.Exterior is not null).Take(2)];
        Assert.Equal(2, towns.Length);

        DaggerfallSiteProfile source = PopulationProfile(ReadInputs(root), towns[0].Id, "population-source");
        DaggerfallSiteProfile destination = SameContentAt(source, towns[1].Id, DaggerfallWorldProfileKind.Exterior, "population-destination");
        DaggerfallSessionComposition composition = new(definitions, source, DaggerfallTuning.Defaults)
        {
            Profiles = new DaggerfallSiteProfiles([source, destination]),
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, source);
        PopulateContent(content, destination);
        SpatialFake spatial = SpatialFake.Create(source.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        DaggerfallNpc civilian = Assert.Single(session.State.Npcs.All.Where(npc => npc.StableKey == "population/source/0/0"));
        long id = civilian.DurableId;
        Assert.Equal("guard", civilian.Role);
        Assert.True(session.State.Actors.TryGet(id, out ActorState? actor));
        Assert.Equal(new WorldPoint(1F, 1F, 1F), actor!.Position);

        actor.Stats.GetTrack(TrackId.Parse("health")).SetCurrent(1, clamp: true);
        session.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).BaseValue = 60;
        for (ulong step = 1; step < 16 && !actor.IsDefeated; step++)
            session.ResolveExplicitMelee(new ExplicitMeleeRequest(1, id, 1, step, .125));
        Assert.True(actor.IsDefeated);
        Assert.True(session.Corpses.ContainsKey(id));

        Assert.True(session.TryTransitionTo(destination.ProfileKey));
        Assert.False(session.State.Actors.TryGet(id, out _));
        Assert.True(session.TryTransitionTo(source.ProfileKey));
        session.ReconcileNpcProjection();
        Assert.True(session.State.Actors.TryGet(id, out ActorState? returned));
        Assert.True(returned!.IsDefeated);
        Assert.True(session.Corpses.ContainsKey(id));

        RulesetSavePayload saved = session.CaptureSave();
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, saved);
        DaggerfallNpc restoredNpc = Assert.Single(restored.State.Npcs.All.Where(npc => npc.StableKey == "population/source/0/0"));
        Assert.Equal(id, restoredNpc.DurableId);
        Assert.True(restored.State.Actors.Get(id).IsDefeated);
        Assert.True(restored.Corpses.ContainsKey(id));
        Assert.Equal(returned.Position, restored.State.Actors.Get(id).Position);
    }

    private static DaggerfallSiteProfile PopulationProfile(DaggerfallSiteProfile source, DaggerfallSiteId site, string logicalId)
    {
        DaggerfallSiteProfile exterior = SameContentAt(source, site, DaggerfallWorldProfileKind.Exterior, logicalId);
        NormalizedBillboardSprite billboard = new("sprite/population.png", Hash, 8, 8,
            [new NormalizedAtlasFrame(0, 0, 0, 8, 8)], 0, new Vector2(.5F, 0F), Vector2.One);
        return new DaggerfallSiteProfile(
            exterior.Project,
            exterior.SpatialArtifact,
            exterior.StaticMesh,
            exterior.WorldAppearance,
            exterior.InitialLook,
            exterior.Materials,
            exterior.ActorSprites,
            exterior.MobileSprites,
            exterior.Audio,
            exterior.ClassicPresentation,
            exterior.Site,
            exterior.Doors,
            exterior.ProfileKind,
            exterior.ProfileKey.LogicalId,
            exterior.Portals,
            exterior.Anchors.Values.ToArray(),
            exterior.Lights,
            exterior.GroundContainerSprite,
            exterior.DungeonMap,
            exterior.DungeonActions,
            exterior.DungeonActionModels,
            exterior.InteriorBuilding,
            exterior.Music,
            exterior.AudioBundle,
            exterior.QuestMarkers,
            billboardSprites: new Dictionary<(int Archive, int Record), NormalizedBillboardSprite>
            {
                [(210, 4)] = billboard,
            },
            population:
            [
                new DaggerfallPopulationPlacement("population/source/0/0", new WorldPoint(1F, 1F, 1F), 210, 4,
                    TestPayload.Definitions.Factions.Factions.Values.First(faction => faction.Type == 10).Id, 0, 7),
            ]);
    }
}
