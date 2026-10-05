using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class StaticNpcAdmissionTests
{
    [Fact]
    public void Static_provider_is_a_real_actor_with_a_site_bound_service_and_save_identity()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile providerSite = StaticProviderSite(ReadProfile(root, content, definitions,
            "daggerfall.charing-interior-1-1-0.json"));
        DaggerfallSiteProfile outside = ReadInputs(root);
        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, providerSite);
        PopulateContent(contentService, outside);
        SpatialFake spatial = SpatialFake.Create(providerSite.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(contentService, spatial.Service, new AppearanceFake(releases));
        DaggerfallSiteProfiles profiles = new([providerSite, outside]);
        DaggerfallSessionComposition composition = new(definitions, providerSite, DaggerfallTuning.Defaults)
        {
            Profiles = profiles,
        };

        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);
        DaggerfallNpc npc = Assert.Single(session.State.Npcs.All, value => value.Kind == DaggerfallNpcKind.Static);
        Assert.Equal(DaggerfallActorKinds.StaticNpc, session.DefinitionsByActor[npc.DurableId].Kind);
        Assert.True(session.State.Actors.TryGet(npc.DurableId, out _));
        Assert.Equal(providerSite.ProfileKey, npc.Profile);
        Assert.Contains("buy-spells", npc.Services);
        Assert.Equal(DaggerfallServiceDenial.None,
            session.State.Services.ProviderAvailable(new(npc.DurableId, npc.Site, "buy-spells")));
        DaggerfallActivationTarget dialogueTarget = Assert.Single(session.Dialogue.NpcTargets());
        Assert.Equal(npc.DurableId, checked((long)dialogueTarget.Identity.Value));
        Assert.True(session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, dialogueTarget)).Applied);

        // Static providers are social actors, not combat/magic targets. An area spell centered on
        // the provider must complete without asking MagicProfile to project the static definition.
        session.State.PlayerControl.MoveTo(session.State.Actors.Get(npc.DurableId).Position.ToVector());
        session.State.Character.LearnSpell("spell.024");
        Track magicka = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
        magicka.Maximum.BaseValue = 1000;
        magicka.SetCurrent(1000);
        Assert.Equal(DaggerfallCastOutcome.Ready, session.ReadyPlayerSpell("spell.024").Outcome);
        Assert.Equal(DaggerfallCastOutcome.Missed,
            session.ReleaseReadySpell(session.State.Actors.Player.DurableId, Vector3.UnitZ).Outcome);

        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        DaggerfallNpcEntry savedNpc = Assert.Single(saved.Npcs.Entries, value => value.DurableId == npc.DurableId);
        Assert.Equal(providerSite.ProfileKey.LogicalId, savedNpc.ProfileId);
        DaggerfallDynamicActorSave savedActor = Assert.Single(saved.DynamicActors, value => value.EntityId == npc.DurableId);
        Assert.Equal(DaggerfallActorKinds.StaticNpc, savedActor.Definition);

        Assert.True(session.TryTransitionTo(outside.ProfileKey));
        Assert.False(session.State.Actors.TryGet(npc.DurableId, out _));
        Assert.True(session.TryTransitionTo(providerSite.ProfileKey));
        DaggerfallNpc returned = session.State.Npcs.Require(npc.DurableId);
        Assert.True(session.State.Actors.TryGet(npc.DurableId, out _));
        Assert.Equal(npc.DurableId, returned.DurableId);
        Assert.Equal(DaggerfallServiceDenial.None,
            session.State.Services.ProviderAvailable(new(returned.DurableId, returned.Site, "buy-spells")));

        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, composition, session.CaptureSave());
        DaggerfallNpc restoredNpc = Assert.Single(restored.State.Npcs.All, value => value.Kind == DaggerfallNpcKind.Static);
        Assert.Equal(npc.DurableId, restoredNpc.DurableId);
        Assert.True(restored.State.Actors.TryGet(restoredNpc.DurableId, out _));
        Assert.Equal(DaggerfallServiceDenial.None,
            restored.State.Services.ProviderAvailable(new(restoredNpc.DurableId, restoredNpc.Site, "buy-spells")));
    }

    [Fact]
    public void Failed_static_appearance_admission_releases_the_candidate_and_can_retry()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile outside = ReadInputs(root);
        DaggerfallSiteProfile providerSite = StaticProviderSite(ReadProfile(root, content, definitions,
            "daggerfall.charing-interior-1-1-0.json"));
        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, outside);
        PopulateContent(contentService, providerSite);
        SpatialFake spatial = SpatialFake.Create(outside.SpatialArtifact.Sha256, releases);
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(contentService, spatial.Service, appearance);
        DaggerfallSiteProfiles profiles = new([outside, providerSite]);
        DaggerfallSessionComposition composition = new(definitions, outside, DaggerfallTuning.Defaults)
        {
            Profiles = profiles,
        };
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);

        // The destination's normalized treasure billboard is admitted by the projection before
        // static source people are materialized; fail the next atlas so the injected error lands
        // in the static actor admission itself.
        int nextAtlas = appearance.AtlasRequests.Count + 2;
        appearance.FailSpriteAtlasCreateAt = nextAtlas;
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => session.TryTransitionTo(providerSite.ProfileKey));

        // The failed destination never commits an NPC current-profile placement or a roster actor.
        DaggerfallNpc candidate = Assert.Single(session.State.Npcs.All, value => value.Kind == DaggerfallNpcKind.Static);
        Assert.Null(candidate.Profile);
        Assert.False(session.State.Actors.TryGet(candidate.DurableId, out _));
        Assert.DoesNotContain(candidate.DurableId, session.DefinitionsByActor.Keys);

        appearance.FailSpriteAtlasCreateAt = 0;
        Assert.True(session.TryTransitionTo(providerSite.ProfileKey));
        DaggerfallNpc admitted = session.State.Npcs.Require(candidate.DurableId);
        Assert.Equal(providerSite.ProfileKey, admitted.Profile);
        Assert.True(session.State.Actors.TryGet(candidate.DurableId, out _));
    }

    [Fact]
    public void Failed_later_static_admission_rolls_back_the_whole_batch_and_can_retry()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        ProductContent content = FullContent(root);
        DaggerfallSiteProfile outside = ReadInputs(root);
        DaggerfallSiteProfile providerSite = StaticProviderSite(ReadProfile(root, content, definitions,
            "daggerfall.charing-interior-1-1-0.json"), placementCount: 2);
        List<string> releases = [];
        ContentFake contentService = new(releases);
        PopulateContent(contentService, outside);
        PopulateContent(contentService, providerSite);
        SpatialFake spatial = SpatialFake.Create(outside.SpatialArtifact.Sha256, releases);
        AppearanceFake appearance = new(releases);
        EngineContextFake engine = EngineContextFake.Create(contentService, spatial.Service, appearance);
        DaggerfallSiteProfiles profiles = new([outside, providerSite]);
        DaggerfallSessionComposition composition = new(definitions, outside, DaggerfallTuning.Defaults)
        {
            Profiles = profiles,
        };
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, composition);

        // One destination atlas is admitted before the static people; fail the second static
        // atlas so the first person has already acquired an actor, binding, placement, and sprite.
        appearance.FailSpriteAtlasCreateAt = appearance.AtlasRequests.Count + 3;
        Assert.Throws<InvalidOperationException>(() => session.TryTransitionTo(providerSite.ProfileKey));

        DaggerfallNpc[] candidates = session.State.Npcs.All.Where(value => value.Kind == DaggerfallNpcKind.Static).ToArray();
        Assert.Equal(2, candidates.Length);
        Assert.All(candidates, candidate =>
        {
            Assert.Null(candidate.Profile);
            Assert.False(session.State.Actors.TryGet(candidate.DurableId, out _));
            Assert.DoesNotContain(candidate.DurableId, session.DefinitionsByActor.Keys);
        });

        appearance.FailSpriteAtlasCreateAt = 0;
        Assert.True(session.TryTransitionTo(providerSite.ProfileKey));
        Assert.All(candidates, candidate =>
        {
            DaggerfallNpc admitted = session.State.Npcs.Require(candidate.DurableId);
            Assert.Equal(providerSite.ProfileKey, admitted.Profile);
            Assert.True(session.State.Actors.TryGet(candidate.DurableId, out _));
        });
    }

    [Fact]
    public void Bank_building_admits_any_catalog_merchant_faction_as_banking_provider()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallFactionDefinition merchant = definitions.Factions.Factions.Values.First(
            faction => faction.SocialGroup == 1 && faction.Id != 510 && faction.SocialGroupName == "Merchants");
        NormalizedBillboardSprite billboard = new("sprite/bank.png", new(1, 2, 3, 4), 1, 1,
            [new NormalizedAtlasFrame(0, 0, 0, 1, 1)], 0, Vector2.Zero, new(1, 1));
        string json = JsonSerializer.Serialize(new
        {
            world = new
            {
                interiorBuilding = new { buildingType = 3, factionId = 0 },
                staticNpcs = new[]
                {
                    new
                    {
                        id = "person/bank",
                        billboardArchive = 211,
                        billboardRecord = 12,
                        race = "breton",
                        gender = "Male",
                        factionId = merchant.Id,
                        nameSeed = 1,
                        position = new { x = 1F, y = 1F, z = 1F },
                    },
                },
            },
        });
        DaggerfallContentDiagnostics diagnostics = new();
        IReadOnlyList<DaggerfallStaticNpcPlacement> placements = DaggerfallStaticNpcPlacement.Read(
            System.Text.Encoding.UTF8.GetBytes(json), new Dictionary<(int Archive, int Record), NormalizedBillboardSprite>
            {
                [(211, 12)] = billboard,
            }, definitions, new DaggerfallSiteId(17, 4), diagnostics);

        diagnostics.ThrowIfAny();
        DaggerfallStaticNpcPlacement placement = Assert.Single(placements);
        Assert.Contains("banking", placement.Services);
        Assert.Equal("bank teller", placement.Role);
    }

    [Fact]
    public void Temple_source_providers_publish_their_catalog_services()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        NormalizedBillboardSprite billboard = new("sprite/temple.png", new(1, 2, 3, 4), 1, 1,
            [new NormalizedAtlasFrame(0, 0, 0, 1, 1)], 0, Vector2.Zero, new(1, 1));
        int[] providerFactions = [254, 496, 497, 498, 810, 813];
        string json = JsonSerializer.Serialize(new
        {
            world = new
            {
                interiorBuilding = new { buildingType = 14, factionId = DaggerfallConcreteGuildCatalog.KynarethFactionId },
                staticNpcs = providerFactions.Select((faction, index) => new
                {
                    id = $"person/provider-{faction}",
                    billboardArchive = 211,
                    billboardRecord = 13,
                    race = "breton",
                    gender = "Female",
                    factionId = faction,
                    nameSeed = index,
                    position = new { x = 1F + index, y = 1F, z = 1F },
                }),
            },
        });

        DaggerfallContentDiagnostics diagnostics = new();
        IReadOnlyList<DaggerfallStaticNpcPlacement> placements = DaggerfallStaticNpcPlacement.Read(
            System.Text.Encoding.UTF8.GetBytes(json), new Dictionary<(int Archive, int Record), NormalizedBillboardSprite>
            {
                [(211, 13)] = billboard,
            }, definitions, new DaggerfallSiteId(17, 4), diagnostics);

        diagnostics.ThrowIfAny();
        Assert.Contains("training", Assert.Single(placements, value => value.Appearance.FactionId == 254).Services);
        Assert.Contains("buy-spells", Assert.Single(placements, value => value.Appearance.FactionId == 496).Services);
        Assert.Contains("make-spells", Assert.Single(placements, value => value.Appearance.FactionId == 497).Services);
        Assert.Contains("daedra-summoning", Assert.Single(placements, value => value.Appearance.FactionId == 498).Services);
        Assert.Contains("donate", Assert.Single(placements, value => value.Appearance.FactionId == 810).Services);
        Assert.Contains("cure-disease", Assert.Single(placements, value => value.Appearance.FactionId == 813).Services);
        Assert.Equal("trainer", Assert.Single(placements, value => value.Appearance.FactionId == 254).Role);
        Assert.Equal("priest", Assert.Single(placements, value => value.Appearance.FactionId == 810).Role);
        Assert.Equal("healer", Assert.Single(placements, value => value.Appearance.FactionId == 813).Role);
    }

    [Fact]
    public void Mages_source_provider_services_publish_without_faction_only_invention()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        NormalizedBillboardSprite billboard = new("sprite/mages.png", new(1, 2, 3, 4), 1, 1,
            [new NormalizedAtlasFrame(0, 0, 0, 1, 1)], 0, Vector2.Zero, new(1, 1));
        string json = JsonSerializer.Serialize(new
        {
            world = new
            {
                interiorBuilding = new { buildingType = 11, factionId = DaggerfallConcreteGuildCatalog.MagesFactionId },
                staticNpcs = new[]
                {
                    new { id = "person/buy", billboardArchive = 211, billboardRecord = 14, race = "breton", gender = "Male", factionId = 60, nameSeed = 1, position = new { x = 1F, y = 1F, z = 1F } },
                    new { id = "person/make", billboardArchive = 211, billboardRecord = 14, race = "breton", gender = "Male", factionId = 64, nameSeed = 2, position = new { x = 2F, y = 1F, z = 1F } },
                    new { id = "person/identify", billboardArchive = 211, billboardRecord = 14, race = "breton", gender = "Male", factionId = 801, nameSeed = 3, position = new { x = 3F, y = 1F, z = 1F } },
                    new { id = "person/make-magic", billboardArchive = 211, billboardRecord = 14, race = "breton", gender = "Male", factionId = 802, nameSeed = 4, position = new { x = 4F, y = 1F, z = 1F } },
                },
            },
        });

        DaggerfallContentDiagnostics diagnostics = new();
        IReadOnlyList<DaggerfallStaticNpcPlacement> placements = DaggerfallStaticNpcPlacement.Read(
            System.Text.Encoding.UTF8.GetBytes(json), new Dictionary<(int Archive, int Record), NormalizedBillboardSprite>
            {
                [(211, 14)] = billboard,
            }, definitions, new DaggerfallSiteId(17, 4), diagnostics);

        diagnostics.ThrowIfAny();
        Assert.Contains("buy-spells", Assert.Single(placements, value => value.Appearance.FactionId == 60).Services);
        Assert.Contains("make-spells", Assert.Single(placements, value => value.Appearance.FactionId == 64).Services);
        Assert.Contains("identify", Assert.Single(placements, value => value.Appearance.FactionId == 801).Services);
        Assert.Contains("make-magic-items", Assert.Single(placements, value => value.Appearance.FactionId == 802).Services);
        Assert.DoesNotContain("banking", placements.Single(value => value.Appearance.FactionId == 801).Services);
    }

    internal static DaggerfallSiteProfile StaticProviderSite(DaggerfallSiteProfile source, int placementCount = 1, int factionId = 60)
    {
        if (placementCount is < 1 or > 2) throw new ArgumentOutOfRangeException(nameof(placementCount));
        NormalizedActorSprite mobile = source.MobileSprites.Values.First();
        const int archive = 211;
        const int record = 12;
        NormalizedBillboardSprite billboard = new(mobile.TexturePath, mobile.TextureSha256, mobile.AtlasWidth,
            mobile.AtlasHeight, mobile.Frames, mobile.InitialFrameId, mobile.Pivot, mobile.Size);
        Dictionary<(int Archive, int Record), NormalizedBillboardSprite> billboards = source.BillboardSprites.ToDictionary();
        billboards[(archive, record)] = billboard;
        DaggerfallInteriorBuilding building = source.InteriorBuilding! with
        {
            BuildingType = 11,
            FactionId = DaggerfallConcreteGuildCatalog.MagesFactionId,
        };
        DaggerfallStaticNpcPlacement[] placements = Enumerable.Range(0, placementCount).Select(index => new DaggerfallStaticNpcPlacement(
            $"person/{index}", new WorldPoint(1F + index, 1F, 1F),
            new("breton", "Male", archive, record, 123 + index, index == 0 ? factionId : 60),
            "spell seller", ["talk", "buy-spells"],
            new(mobile.TexturePath, mobile.TextureSha256, mobile.AtlasWidth, mobile.AtlasHeight,
                mobile.Frames, mobile.InitialFrameId, mobile.Pivot, mobile.Size))).ToArray();
        return new DaggerfallSiteProfile(source.Project, source.SpatialArtifact, source.StaticMesh, source.WorldAppearance,
            source.InitialLook, source.Materials, source.ActorSprites, source.MobileSprites, source.Audio,
            source.ClassicPresentation, source.Site, source.Doors, source.ProfileKind, source.ProfileKey.LogicalId,
            source.Portals, source.Anchors.Values.ToArray(), source.Lights, source.GroundContainerSprite,
            source.DungeonMap, source.DungeonActions, source.DungeonActionModels, building, source.Music,
            source.AudioBundle, source.QuestMarkers, billboards, placements);
    }
}
