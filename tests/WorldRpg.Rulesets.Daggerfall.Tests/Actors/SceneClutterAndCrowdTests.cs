using System.Text;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Ai;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Focused tests for #11017: seeded scene-clutter people out of scenery, <see cref="ActorRenderComponent"/>-driven
/// crowd dispatch, 3-state <see cref="ActorInteractionPolicyKind"/> (<c>None</c> / <c>AmbientBark</c> /
/// <c>FullDialogue</c>), and pre-visit <see cref="ActorOriginKind.SeededSlot"/> referrals with zero neighbor ledger bytes.
/// </summary>
public sealed class SceneClutterAndCrowdTests
{
    [Fact]
    public void Person_clutter_billboards_are_excluded_from_decorations_and_materialize_as_real_EntityStore_actors()
    {
        using var fixture = new SanguineRoseSessionTests.Fixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteProfile active = fixture.Inputs;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteId site = active.Site!.Value;

        var personFlat = (Archive: 184, Record: 6);
        var propFlat = (Archive: 197, Record: 6);
        var furnitureFlat = (Archive: 210, Record: 0);

        // ReadDecorations never admits person-archive clutter billboards as decorative scenery,
        // while keeping non-person props (including (197, 6)).
        Dictionary<(int Archive, int Record), NormalizedBillboardSprite> sprites = new(active.BillboardSprites);
        NormalizedBillboardSprite sample = sprites.Values.First();
        sprites.TryAdd(personFlat, sample);
        sprites.TryAdd((182, 34), sample);
        sprites.TryAdd((186, 4), sample);
        sprites.TryAdd((197, 3), sample);
        sprites.TryAdd(propFlat, sample);
        sprites.TryAdd(furnitureFlat, sample);

        byte[] normalizedJson = Encoding.UTF8.GetBytes("""
            {
              "world": {
                "billboards": [
                  { "id": "billboard/clutter-184", "spriteResourceId": "sprite/texture-184-6", "position": { "x": 10.0, "y": 0.0, "z": -5.0 }, "baseAligned": true },
                  { "id": "billboard/clutter-182", "spriteResourceId": "sprite/texture-182-34", "position": { "x": 11.0, "y": 0.0, "z": -5.0 }, "baseAligned": true },
                  { "id": "billboard/clutter-186", "spriteResourceId": "sprite/texture-186-4", "position": { "x": 12.0, "y": 0.0, "z": -5.0 }, "baseAligned": true },
                  { "id": "billboard/clutter-197-person", "spriteResourceId": "sprite/texture-197-3", "position": { "x": 13.0, "y": 0.0, "z": -5.0 }, "baseAligned": true },
                  { "id": "billboard/prop-197-6", "spriteResourceId": "sprite/texture-197-6", "position": { "x": 14.0, "y": 0.0, "z": -5.0 }, "baseAligned": true },
                  { "id": "billboard/prop-210-0", "spriteResourceId": "sprite/texture-210-0", "position": { "x": 15.0, "y": 0.0, "z": -5.0 }, "baseAligned": true }
                ],
                "staticNpcs": [
                  {
                    "id": "person/tvrnbl02-rmb/0/0/e-c00",
                    "position": { "x": 20.0, "y": 0.0, "z": 20.0 },
                    "billboardArchive": 184,
                    "billboardRecord": 6,
                    "factionId": 0,
                    "deriveRegionalFaction": true,
                    "race": null,
                    "gender": "Female",
                    "nameSeed": 4242,
                    "interaction": "none",
                    "harm": "law-protected-civilian",
                    "spawnerId": "clutter-person"
                  }
                ]
              }
            }
            """);

        DaggerfallContentDiagnostics diagnostics = new();
        IReadOnlyList<DaggerfallSiteDecoration> decorations = DaggerfallSiteContent.ReadDecorations(
            normalizedJson, sprites, DaggerfallWorldProfileKind.Exterior, climate: 226, diagnostics);
        diagnostics.ThrowIfAny();
        Assert.Equal(["billboard/prop-197-6", "billboard/prop-210-0"], decorations.Select(d => d.Id).ToArray());

        IReadOnlyList<DaggerfallStaticNpcPlacement> placements = DaggerfallStaticNpcPlacement.Read(
            normalizedJson, sprites, definitions, site, diagnostics);
        diagnostics.ThrowIfAny();
        DaggerfallStaticNpcPlacement clutterPlacement = Assert.Single(placements);
        Assert.Equal(ActorInteractionPolicyKind.None, clutterPlacement.Interaction);
        Assert.Equal(ActorHarmPolicyKind.LawProtectedCivilian, clutterPlacement.Harm);
        Assert.Equal("clutter-person", clutterPlacement.SpawnerId);
        Assert.Empty(clutterPlacement.Services);

        int? expectedRegionalFaction = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, site.Region)?.Id;
        Assert.NotNull(expectedRegionalFaction);
        Assert.Equal(expectedRegionalFaction.Value, clutterPlacement.Appearance.FactionId);

        // Materialize in a live session and verify real EntityStore actor, QueryNearby, components, and 0 ledger bytes.
        DaggerfallSiteProfile clutterProfile = WithStaticNpcs(active, [clutterPlacement]);
        session.Roster.MaterializeStaticNpcs(clutterProfile);

        long clutterId = DaggerfallPeopleSlots.SeededSlotIdentity(
            clutterProfile.Site!.Value, $"{clutterProfile.ProfileKey.LogicalId}/{clutterPlacement.Id}");
        Assert.True(session.State.Actors.TryGet(clutterId, out ActorState? clutterActor));
        Assert.True(clutterActor.Actor.Has<StatsComponent>());
        Assert.Equal(expectedRegionalFaction.Value, clutterActor.Actor.Get<FactionComponent>().RegionalPeopleFactionId);
        Assert.Equal(expectedRegionalFaction.Value, clutterActor.Actor.Get<FactionComponent>().PrimaryFactionId);
        Assert.Equal(ActorHarmPolicyKind.LawProtectedCivilian, clutterActor.Actor.Get<ActorHarmPolicy>().Policy);
        Assert.Equal(ActorInteractionPolicyKind.None, clutterActor.Actor.Get<ActorInteractionPolicy>().Policy);
        Assert.Equal(ActorVisualKind.Billboard, clutterActor.Actor.Get<ActorRenderComponent>().Spec.Kind);

        // Present in QueryNearby as a real world actor, yet never offered by Dialogue.NpcTargets().
        IReadOnlyList<NearbyActorHit> nearby = session.QueryNearby(clutterPlacement.Position, radius: 3d);
        Assert.Contains(nearby, hit => hit.Actor.DurableId == clutterId);
        Assert.DoesNotContain(session.Dialogue.NpcTargets(), target => (long)target.Identity.Value == clutterId);

        // Untouched seeded clutter person writes zero bytes to ActorLedger on save.
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.DoesNotContain(saved.ActorLedger, entry => entry.DurableId == clutterId);
    }

    [Fact]
    public void Three_state_ActorInteractionPolicy_governs_None_AmbientBark_and_FullDialogue()
    {
        using var fixture = new SanguineRoseSessionTests.Fixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteProfile active = fixture.Inputs;
        var address = active.BillboardSprites.Keys.First();
        NormalizedActorSprite sprite = active.BillboardSprites[address].Standing(address);

        DaggerfallStaticNpcPlacement noneSlot = new(
            "person/test/clutter-none",
            new WorldPoint(10f, 0f, 10f),
            new DaggerfallNpcAppearance("breton", "Female", address.Archive, address.Record, 101, 0),
            "clutter-person",
            [],
            sprite,
            ActorInteractionPolicyKind.None,
            ActorHarmPolicyKind.LawProtectedCivilian,
            "clutter-person",
            DeriveRegionalFaction: true);

        DaggerfallStaticNpcPlacement barkSlot = new(
            "person/test/bystander-bark",
            new WorldPoint(12f, 0f, 10f),
            new DaggerfallNpcAppearance("breton", "Male", address.Archive, address.Record, 202, 0),
            "bystander",
            ["bark"],
            sprite,
            ActorInteractionPolicyKind.AmbientBark,
            ActorHarmPolicyKind.LawProtectedCivilian,
            "bystander",
            DeriveRegionalFaction: true);

        DaggerfallStaticNpcPlacement talkSlot = new(
            "person/test/merchant-talk",
            new WorldPoint(14f, 0f, 10f),
            new DaggerfallNpcAppearance("breton", "Female", address.Archive, address.Record, 303, 0),
            "person",
            ["talk"],
            sprite,
            ActorInteractionPolicyKind.FullDialogue,
            ActorHarmPolicyKind.Protected,
            "town-resident",
            DeriveRegionalFaction: true);

        DaggerfallSiteProfile profile = WithStaticNpcs(active, [noneSlot, barkSlot, talkSlot]);
        session.Roster.MaterializeStaticNpcs(profile);

        DaggerfallSiteId siteId = profile.Site!.Value;
        long noneId = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, $"{profile.ProfileKey.LogicalId}/{noneSlot.Id}");
        long barkId = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, $"{profile.ProfileKey.LogicalId}/{barkSlot.Id}");
        long talkId = DaggerfallPeopleSlots.SeededSlotIdentity(siteId, $"{profile.ProfileKey.LogicalId}/{talkSlot.Id}");

        DaggerfallActivationTarget[] targets = [.. session.Dialogue.NpcTargets()];
        Assert.DoesNotContain(targets, t => (long)t.Identity.Value == noneId);
        DaggerfallActivationTarget barkTarget = Assert.Single(targets, t => (long)t.Identity.Value == barkId);
        DaggerfallActivationTarget talkTarget = Assert.Single(targets, t => (long)t.Identity.Value == talkId);

        // Direct activation against a None actor refuses cleanly.
        ActorState noneActor = session.State.Actors.Get(noneId);
        DaggerfallActivationTarget syntheticNoneTarget = new(
            DaggerfallActivationTargetKind.Npc,
            ActorsState.Identity(noneId),
            noneActor.Actor.Entity,
            checked((ulong)noneId),
            noneActor.Position,
            Precedence: 1,
            Label: "clutter-person");
        DaggerfallActivationOutcome noneOutcome = session.Dialogue.ActivateNpc(
            new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, syntheticNoneTarget));
        Assert.False(noneOutcome.Applied);
        Assert.Null(session.Dialogue.CurrentNpc());

        // AmbientBark emits a test-data bark line and marks Met without opening the dialogue modal.
        DaggerfallActivationOutcome barkOutcome = session.Dialogue.ActivateNpc(
            new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, barkTarget));
        Assert.True(barkOutcome.Applied);
        Assert.Contains("\"", barkOutcome.Message, StringComparison.Ordinal);
        Assert.Null(session.Dialogue.CurrentNpc());
        Assert.True(session.State.Actors.Get(barkId).Actor.Get<ActorOverlayComponent>().Met);

        // FullDialogue opens the conversation modal.
        DaggerfallActivationOutcome talkOutcome = session.Dialogue.ActivateNpc(
            new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, talkTarget));
        Assert.True(talkOutcome.Applied);
        Assert.NotNull(session.Dialogue.CurrentNpc());
        Assert.Equal(talkId, session.Dialogue.CurrentNpc()!.DurableId);
    }

    [Fact]
    public void Crowd_dispatch_reads_ActorRenderComponent_independently_of_gameplay_kind_and_swaps_in_place()
    {
        using var fixture = new SanguineRoseSessionTests.Fixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteProfile active = fixture.Inputs;
        var address = active.BillboardSprites.Keys.First();
        NormalizedActorSprite sprite = active.BillboardSprites[address].Standing(address);

        // Create a non-civilian static placement (no WanderMemoryComponent, EntityTypeId == StaticNpc).
        DaggerfallStaticNpcPlacement staticSlot = new(
            "person/test/crowd-visual",
            new WorldPoint(16f, 0f, 16f),
            new DaggerfallNpcAppearance("breton", "Female", address.Archive, address.Record, 404, 0),
            "clutter-person",
            [],
            sprite,
            ActorInteractionPolicyKind.None,
            ActorHarmPolicyKind.LawProtectedCivilian,
            "clutter-person",
            DeriveRegionalFaction: true);

        DaggerfallSiteProfile profile = WithStaticNpcs(active, [staticSlot]);
        session.Roster.MaterializeStaticNpcs(profile);

        long actorId = DaggerfallPeopleSlots.SeededSlotIdentity(
            profile.Site!.Value, $"{profile.ProfileKey.LogicalId}/{staticSlot.Id}");
        ActorState actor = session.State.Actors.Get(actorId);
        Assert.False(actor.Actor.Has<WanderMemoryComponent>());
        Assert.Equal(DaggerfallActorKinds.StaticNpc, actor.Actor.TypeId.Value);

        RecordingWalkerCrowd crowd = new();
        DaggerfallSiteAppearance appearance = session.ActiveAppearance;
        appearance.UsePeopleRigs(() => crowd);

        // Baseline: Billboard render spec does NOT dispatch through IWalkerCrowd.
        List<AppearanceFact> facts = [];
        appearance.AppendActorFacts(facts, session.State.Actors, DaggerfallPresentationObjectIds.ActiveLocationSlot);
        Assert.DoesNotContain(crowd.ShownActors, id => id == actorId);

        // Mutate Tier-2 ActorRenderComponent to SplatCharacter on the same non-civilian entity:
        // crowd dispatch activates without requiring Civilian kind or WanderMemoryComponent.
        actor.Actor.Get<ActorRenderComponent>().SetVisual(
            ActorVisualSpec.SplatCivilian(address.Archive, address.Record, "Female", crowdHint: "385"));
        facts.Clear();
        crowd.ShownActors.Clear();
        appearance.AppendActorFacts(facts, session.State.Actors, DaggerfallPresentationObjectIds.ActiveLocationSlot);
        Assert.Contains(crowd.ShownActors, id => id == actorId);
        Assert.False(crowd.LastWalking[actorId]);

        // DaggerfallPeopleSwitch resolves the splat character from ActorRenderComponent.CrowdCharacterHint
        // even when townsperson(id) returns null, and stops when ActorRenderComponent swaps to DirectionalMobile.
        DaggerfallArtReplacements art = DaggerfallPeopleSwitchTests.Art();
        AnimationRecorder animation = AnimationRecorder.Create();
        animation.Clips = DaggerfallPeopleSwitchTests.Clips(art.SplatPeople!);
        using DaggerfallPeopleSwitch peopleSwitch = new(
            art.PeopleRig,
            art.SplatPeople,
            DaggerfallPeopleSwitchTests.Shipped(),
            () => PartsRigsRecorder.Create().Service,
            () => animation.Service,
            () => new ContentFake([]),
            walkerSpeed: 1.3f,
            DaggerfallSplatPeopleTuning.Default,
            session.State.Actors,
            new DaggerfallPeopleStage(session.State.Actors, _ => false, session.State.PlayerControl, (at, _) => WorldPoint.From(at)),
            [],
            townsperson: _ => null,
            seed: _ => 7,
            refreshed: () => true,
            warn: _ => { });

        Appearance? splatShown = peopleSwitch.Show(9001, actorId, 1.7f, walking: false);
        Assert.NotNull(splatShown);

        // Mutate back to DirectionalMobile: both DaggerfallSiteAppearance and DaggerfallPeopleSwitch stop crowd dispatch.
        actor.Actor.Get<ActorRenderComponent>().SetVisual(ActorVisualSpec.DirectionalMobile(146, "Female"));
        Assert.Null(peopleSwitch.Show(9001, actorId, 1.7f, walking: false));

        facts.Clear();
        crowd.ShownActors.Clear();
        appearance.AppendActorFacts(facts, session.State.Actors, DaggerfallPresentationObjectIds.ActiveLocationSlot);
        Assert.DoesNotContain(crowd.ShownActors, id => id == actorId);
    }

    [Fact]
    public void Real_assembled_town_with_tavern_clutter_block_emits_clutter_person_out_of_decorations()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;

        // 1. Real Arena2 town with TVRNBL04/TVRNGL04 -> non-talkable (None) clutter person out of Decorations.
        DaggerfallSiteRecord noneTown = definitions.Locations.Records.First(record =>
            record.Exterior is { Blocks: { Count: > 0 } blocks }
            && blocks.Any(b => b.SourceName is "TVRNBL04.RMB" or "TVRNGL04.RMB"));
        DaggerfallSiteProfile assembledNone = Assembled(DaggerfallWorldProfileIds.Exterior(noneTown.Id));

        Assert.DoesNotContain(assembledNone.Decorations, dec =>
            DaggerfallPeopleSlots.IsPersonClutterBillboard(dec.Source.Archive, dec.Source.Record));
        DaggerfallStaticNpcPlacement noneClutter = Assert.Single(assembledNone.StaticNpcs, npc =>
            npc.SpawnerId == "clutter-person"
            && npc.Interaction == ActorInteractionPolicyKind.None
            && npc.Harm == ActorHarmPolicyKind.LawProtectedCivilian);
        int expectedNoneFaction = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, noneTown.Id.Region)!.Id;
        Assert.Equal(expectedNoneFaction, noneClutter.Appearance.FactionId);
        Assert.Empty(noneClutter.Services);

        // 2. Real Arena2 town with TVRNBL02/TVRNGL02 -> AmbientBark clutter bystander out of Decorations.
        DaggerfallSiteRecord barkTown = definitions.Locations.Records.First(record =>
            record.Exterior is { Blocks: { Count: > 0 } blocks }
            && blocks.Any(b => b.SourceName is "TVRNBL02.RMB" or "TVRNGL02.RMB"));
        DaggerfallSiteProfile assembledBark = Assembled(DaggerfallWorldProfileIds.Exterior(barkTown.Id));

        Assert.DoesNotContain(assembledBark.Decorations, dec =>
            DaggerfallPeopleSlots.IsPersonClutterBillboard(dec.Source.Archive, dec.Source.Record));
        DaggerfallStaticNpcPlacement barkClutter = Assert.Single(assembledBark.StaticNpcs, npc =>
            npc.SpawnerId == "clutter-person"
            && npc.Interaction == ActorInteractionPolicyKind.AmbientBark
            && npc.Harm == ActorHarmPolicyKind.LawProtectedCivilian);
        int expectedBarkFaction = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, barkTown.Id.Region)!.Id;
        Assert.Equal(expectedBarkFaction, barkClutter.Appearance.FactionId);
        Assert.Contains("bark", barkClutter.Services);
    }

    [Fact]
    public void Pre_visit_slot_referral_via_real_dialogue_survives_save_reload_and_resolves_real_AmbientBark_slot_with_zero_neighbor_bytes()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteRecord charingRecord = definitions.Locations.Records.Single(record => record.Id == Charing);
        var nearest = new DaggerfallSlotReferralPolicy(definitions, new DaggerfallActorLedger())
            .FindNearestReferralTarget(charingRecord);
        Assert.NotNull(nearest);

        (DaggerfallSiteRecord targetRecord, DaggerfallSiteBlock targetBlock, DaggerfallPeopleSlotDefinition targetSlot) = nearest!.Value;
        DaggerfallWorldProfileKey charingKey = DaggerfallWorldProfileIds.Exterior(charingRecord.Id);
        DaggerfallWorldProfileKey targetProfileKey = DaggerfallWorldProfileIds.Exterior(targetRecord.Id);

        DaggerfallSiteProfile charingProfile = Assembled(charingKey);
        DaggerfallSiteProfile targetProfile = Assembled(targetProfileKey);
        DaggerfallSiteProfiles profiles = new([charingProfile, targetProfile]);
        DaggerfallSessionComposition composition = new(definitions, charingProfile, DaggerfallTuning.Defaults)
        {
            Profiles = profiles,
        };

        EngineContextFake CreateEngine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, charingProfile);
            PopulateContent(content, targetProfile);
            SpatialFake spatial = SpatialFake.Create(charingProfile.Geometry.Spatial[0].Sha256, releases);
            AppearanceFake appearance = new(releases);
            return EngineContextFake.Create(content, spatial.Service, appearance);
        }

        string expectedSlotId = DaggerfallPeopleSlots.PlacedExteriorBlockPersonId(
            targetBlock.SourceName, targetBlock.X, targetBlock.Y, targetSlot.SlotId);
        long expectedMaraId = DaggerfallPeopleSlots.SeededSlotIdentity(
            targetRecord.Id, $"{targetProfileKey.LogicalId}/{expectedSlotId}");
        string expectedTag = $"referred-from:{charingRecord.Name}";

        // Collect real untouched neighbor seeded IDs in the target town (other static/clutter slots + exterior population slots).
        long[] targetNeighborIds =
        [
            .. targetProfile.StaticNpcs
                .Where(s => !StringComparer.Ordinal.Equals(s.Id, expectedSlotId))
                .Select(s => DaggerfallPeopleSlots.SeededSlotIdentity(targetRecord.Id, $"{targetProfileKey.LogicalId}/{s.Id}")),
            .. targetProfile.Population
                .Select(p => DaggerfallPeopleSlots.SeededSlotIdentity(targetRecord.Id, $"{targetProfileKey.LogicalId}/{p.Id}")),
        ];
        Assert.NotEmpty(targetNeighborIds);

        RulesetSavePayload savedBeforeVisit;
        using (DaggerfallSession first = DaggerfallSession.StartNew(CreateEngine().Context, composition))
        {
            // Advance to daytime so Charing's real townspeople are live and talkable.
            first.AdvanceElapsedTime(8 * 60 * 60);
            DaggerfallActivationTarget charingSpeaker = first.Dialogue.NpcTargets().First();

            // Open dialogue with a real Charing townsperson and ask the real "referral" topic.
            DaggerfallActivationOutcome opened = first.Dialogue.ActivateNpc(
                new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, charingSpeaker));
            Assert.True(opened.Applied);
            DaggerfallDialogueView openingView = Assert.IsType<DaggerfallDialogueView>(first.ActivationPanel.View.Dialogue);
            Assert.Contains(openingView.Topics, topic => topic.Id == "referral");

            DaggerfallActivationOutcome referralReply = first.Dialogue.Ask(openingView.Revision, "referral", null, null);
            Assert.True(referralReply.Applied);
            Assert.Contains(targetRecord.Name, referralReply.Message, StringComparison.Ordinal);
            Assert.Contains("Mara", referralReply.Message, StringComparison.Ordinal);
            first.Dialogue.Close();

            // Save BEFORE ever visiting targetProfileKey: only the referred slot enters ActorLedger for the target town.
            savedBeforeVisit = first.CaptureSave();
            DaggerfallSavePayload decodedBefore = DaggerfallSavePayload.Read(savedBeforeVisit);
            ActorLedgerEntry referralEntry = Assert.Single(
                decodedBefore.ActorLedger,
                entry => entry.DurableId == expectedMaraId);
            Assert.Equal(ActorOriginKind.SeededSlot, referralEntry.Origin.Kind);
            Assert.Equal(expectedSlotId, referralEntry.Origin.SlotKey);
            Assert.NotNull(referralEntry.Referral);
            Assert.Contains(expectedTag, referralEntry.Referral!.Tags);
            Assert.Equal("Mara", referralEntry.Referral.DisplayNameOverride);
            foreach (long neighborId in targetNeighborIds)
                Assert.DoesNotContain(decodedBefore.ActorLedger, entry => entry.DurableId == neighborId);
        }

        // Reload BEFORE visiting targetProfileKey, then travel to the real assembled target town for the FIRST time.
        using (DaggerfallSession reloaded = DaggerfallSession.Restore(CreateEngine().Context, composition, savedBeforeVisit))
        {
            Assert.True(reloaded.State.ActorLedger.TryGet(expectedMaraId, out ActorLedgerEntry? restoredEntry));
            Assert.NotNull(restoredEntry!.Referral);

            Assert.True(reloaded.TryTransitionTo(targetProfileKey));
            reloaded.AdvanceElapsedTime(60);

            // Exact pre-visit durable ID resolves to the real TVRNBL02/TVRNGL02 AmbientBark bystander with hydrated name & tag.
            Assert.True(reloaded.State.Actors.TryGet(expectedMaraId, out ActorState? maraActor));
            Assert.True(maraActor.Actor.Has<StatsComponent>());
            Assert.Equal(ActorInteractionPolicyKind.AmbientBark, maraActor.Actor.Get<ActorInteractionPolicy>().Policy);
            Assert.Equal(ActorHarmPolicyKind.LawProtectedCivilian, maraActor.Actor.Get<ActorHarmPolicy>().Policy);
            int expectedTargetRegionFaction = DaggerfallPeopleSlots.ResolveRegionalPeopleFaction(definitions.Factions, targetRecord.Id.Region)!.Id;
            Assert.Equal(expectedTargetRegionFaction, maraActor.Actor.Get<FactionComponent>().RegionalPeopleFactionId);

            DaggerfallNpc maraNpc = reloaded.State.Npcs.Require(expectedMaraId);
            Assert.Equal("Mara", maraNpc.DisplayName);
            Assert.True(maraActor.Actor.Get<ActorOverlayComponent>().HasReferralTag(expectedTag));

            // Real target-town neighbors are also live in EntityStore.
            long[] liveNeighbors = [.. targetNeighborIds.Where(id => reloaded.State.Actors.TryGet(id, out _))];
            Assert.NotEmpty(liveNeighbors);

            // Activating Mara triggers her referral-aware AmbientBark without opening a full dialogue modal.
            DaggerfallActivationTarget maraTarget = Assert.Single(
                reloaded.Dialogue.NpcTargets(),
                t => (long)t.Identity.Value == expectedMaraId);
            DaggerfallActivationOutcome barkOutcome = reloaded.Dialogue.ActivateNpc(
                new DaggerfallActivationSelection(DaggerfallActivationMode.Talk, maraTarget));
            Assert.True(barkOutcome.Applied);
            Assert.Contains("Mara", barkOutcome.Message, StringComparison.Ordinal);
            Assert.Contains(expectedTag, barkOutcome.Message, StringComparison.Ordinal);
            Assert.Null(reloaded.Dialogue.CurrentNpc());

            // Saving after first visit keeps Mara's referral entry while ALL untouched target-town neighbors write 0 ledger bytes.
            DaggerfallSavePayload savedAfterVisit = DaggerfallSavePayload.Read(reloaded.CaptureSave());
            ActorLedgerEntry afterEntry = Assert.Single(
                savedAfterVisit.ActorLedger,
                entry => entry.DurableId == expectedMaraId);
            Assert.NotNull(afterEntry.Referral);
            Assert.Contains(expectedTag, afterEntry.Referral!.Tags);
            foreach (long neighborId in targetNeighborIds)
                Assert.DoesNotContain(savedAfterVisit.ActorLedger, entry => entry.DurableId == neighborId);
        }
    }

    private static DaggerfallSiteProfile WithStaticNpcs(
        DaggerfallSiteProfile template,
        IReadOnlyList<DaggerfallStaticNpcPlacement> staticNpcs) =>
        WithSiteAndStaticNpcs(template, template.Site!.Value, template.ProfileKey, staticNpcs);

    private static DaggerfallSiteProfile WithSiteAndStaticNpcs(
        DaggerfallSiteProfile template,
        DaggerfallSiteId site,
        DaggerfallWorldProfileKey key,
        IReadOnlyList<DaggerfallStaticNpcPlacement> staticNpcs) =>
        new(
            template.Project,
            template.Geometry,
            template.WorldAppearance,
            template.InitialLook,
            template.Materials,
            template.ActorSprites,
            template.MobileSprites,
            template.Audio,
            template.ClassicPresentation,
            site,
            template.Doors,
            key.Kind,
            key.LogicalId,
            template.Portals,
            [.. template.Anchors.Values],
            template.Lights,
            template.GroundContainerSprites,
            template.DungeonMap,
            template.DungeonActions,
            template.DungeonActionModels,
            template.InteriorBuilding,
            template.QuestMarkers,
            template.BillboardSprites,
            staticNpcs,
            template.WaterVolumes,
            template.TerrainTextures,
            template.Population);

    private sealed class RecordingWalkerCrowd : IWalkerCrowd
    {
        internal List<long> ShownActors { get; } = [];
        internal Dictionary<long, bool> LastWalking { get; } = [];

        public Appearance? Show(ulong id, long actor, float heightMetres, bool walking)
        {
            ShownActors.Add(actor);
            LastWalking[actor] = walking;
            return null;
        }

        public void Publishing() { }
        public void Settle() { }
    }
}
