using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Focused tests for #11015: single sparse ActorLedger, Tier-2 ActorRenderComponent, capability policies,
/// 22 authored creature-family factions, unified QueryNearby, and HUD-only WithParty escorts.
/// </summary>
public sealed class DaggerfallActorLedgerTests
{
    [Fact]
    public void ActorLedger_sparse_diff_self_prunes_untouched_slots_to_zero_bytes_and_suppresses_home_duplication()
    {
        DaggerfallActorLedger ledger = new();
        DaggerfallWorldProfileKey townA = DaggerfallWorldProfileIds.Exterior(new DaggerfallSiteId(17, 0));
        DaggerfallWorldProfileKey townB = DaggerfallWorldProfileIds.Exterior(new DaggerfallSiteId(17, 1));
        ActorOrigin slotOrigin = ActorOrigin.Seeded(townA, "rmb:17:0:b0:p00");
        ActorOrigin castOrigin = ActorOrigin.Cast("cast.king-gothryd");

        // Untouched deterministic slots and cast individuals write zero entries (0 bytes).
        Assert.False(ledger.Upsert(new ActorLedgerEntry(5001, slotOrigin)));
        Assert.False(ledger.Upsert(new ActorLedgerEntry(5002, castOrigin)));
        Assert.Equal(0, ledger.Count);
        Assert.Empty(ledger.Capture());
        Assert.False(ledger.IsHomeSlotSuppressed(5001, townA));

        // Mutating pose creates a sparse entry; returning deltas to null self-prunes back to zero.
        Assert.True(ledger.Upsert(new ActorLedgerEntry(5001, slotOrigin, Pose: new ActorPoseOverride(4f, 0f, 8f, 1.5f))));
        Assert.Equal(1, ledger.Count);
        Assert.False(ledger.IsHomeSlotSuppressed(5001, townA));

        Assert.False(ledger.Upsert(new ActorLedgerEntry(5001, slotOrigin)));
        Assert.Equal(0, ledger.Count);
        Assert.Empty(ledger.Capture());

        // Relocating to another site, joining WithParty, or marking Removed suppresses home-slot admission.
        Assert.True(ledger.Upsert(new ActorLedgerEntry(5001, slotOrigin, Location: ActorLocationOverride.AtSite(townB))));
        Assert.True(ledger.IsHomeSlotSuppressed(5001, townA));
        Assert.False(ledger.IsHomeSlotSuppressed(5001, townB));
        Assert.Single(ledger.EntriesAtSite(townB));

        ledger.SetWithParty(5001, ActorPartyRole.Escort, escortPortrait: "face-01", escortOrder: 1);
        Assert.True(ledger.IsHomeSlotSuppressed(5001, townA));
        Assert.Single(ledger.EntriesWithParty());

        ledger.MarkRemoved(5001, slotOrigin);
        Assert.True(ledger.IsRemoved(5001));
        Assert.True(ledger.IsHomeSlotSuppressed(5001, townA));
        Assert.Empty(ledger.EntriesWithParty());
    }

    [Fact]
    public void Creature_factions_load_all_22_classic_teams_and_govern_hostility_and_actor_components()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallCreatureFactionSet factions = definitions.CreatureFactions;

        Assert.Equal(22, factions.Factions.Count);
        Assert.True(factions.TryGet("player-ally", out _));
        Assert.True(factions.TryGet("undead", out _));
        Assert.True(factions.TryGet("daedra", out _));
        Assert.True(factions.TryGet("city-watch", out _));
        Assert.True(factions.TryGet("tigers", out _));

        // Same team never infights; distinct teams are hostile; pacified actors leave player-ally alone.
        Assert.False(factions.AreTeamsHostile("undead", "undead", observerPacified: false));
        Assert.True(factions.AreTeamsHostile("undead", "daedra", observerPacified: false));
        Assert.False(factions.AreTeamsHostile("undead", "player-ally", observerPacified: true));
        Assert.True(factions.AreTeamsHostile("undead", "player-ally", observerPacified: false));

        // Spawned monsters carry FactionComponent, Combatant harm policy, and DirectionalMobile render spec.
        using var fixture = new ConditionSessionFixture();
        long ratId = fixture.Session.SpawnActor("rat", new ActorPose(new WorldPoint(4f, 0f, 4f), 0f));
        ActorState rat = fixture.Session.State.Actors.Get(ratId);
        Assert.True(rat.Actor.Has<StatsComponent>());
        Assert.Equal(ActorHarmPolicyKind.Combatant, rat.Actor.Get<ActorHarmPolicy>().Policy);
        Assert.True(rat.Actor.Get<ActorHarmPolicy>().CanDeflectAttacks);
        Assert.Equal(ActorInteractionPolicyKind.None, rat.Actor.Get<ActorInteractionPolicy>().Policy);
        Assert.Equal(ActorVisualKind.DirectionalMobile, rat.Actor.Get<ActorRenderComponent>().Spec.Kind);
        Assert.Equal(fixture.Session.DefinitionsByActor[ratId].Team, rat.Actor.Get<FactionComponent>().CreatureFactionId);
    }

    [Fact]
    public void Unified_QueryNearby_returns_active_scene_combatants_civilians_and_questors_sorted_nearest_first()
    {
        using var fixture = new SanguineRoseSessionTests.Fixture();
        DaggerfallSession session = fixture.Session;
        DaggerfallSiteProfile profile = fixture.Inputs;
        var address = profile.BillboardSprites.Keys.First();
        DaggerfallSiteRecord site = TestPayload.Definitions.Locations.Records.Single(record => record.Id == profile.Site!.Value);
        DaggerfallNpcSite npcSite = new(site.Region, site.Name, string.Empty, profile.ProfileKey.LogicalId);
        DaggerfallNpcAppearance look = new("breton", "Female", address.Archive, address.Record, 11, 0);

        WorldPoint origin = new(50f, 0f, 50f);
        long ratId = session.SpawnActor("rat", new ActorPose(new WorldPoint(56f, 0f, 50f), 0f)); // distance 6
        long civId = session.State.Npcs.RegisterCivilian(npcSite, look, "townsperson", ["talk"]);
        session.Civilians.Materialize(civId, new ActorPose(new WorldPoint(52f, 0f, 50f), 0f)); // distance 2
        session.State.Npcs.Bind(civId, profile.ProfileKey);

        long questorId = session.State.Npcs.RegisterStable(DaggerfallNpcKind.Questor, "nearby-questor", npcSite, look, "quest person", ["talk"]);
        session.State.Npcs.Place(questorId, profile.ProfileKey, new WorldPoint(54f, 0f, 50f)); // distance 4
        session.ReconcileNpcProjection();

        IReadOnlyList<NearbyActorHit> hits = session.QueryNearby(origin, radius: 10d);
        Assert.Equal([civId, questorId, ratId], hits.Select(hit => hit.Actor.DurableId).ToArray());

        // Capability policies and regional people faction are live on all people in EntityStore.
        ActorState civActor = session.State.Actors.Get(civId);
        ActorState questorActor = session.State.Actors.Get(questorId);
        Assert.Equal(ActorHarmPolicyKind.LawProtectedCivilian, civActor.Actor.Get<ActorHarmPolicy>().Policy);
        Assert.False(civActor.Actor.Get<ActorHarmPolicy>().CanDeflectAttacks);
        Assert.Equal(ActorHarmPolicyKind.Protected, questorActor.Actor.Get<ActorHarmPolicy>().Policy);
        Assert.False(session.SpellTargets.IsEligible(questorId));
        Assert.True(session.SpellTargets.IsEligible(ratId));
        Assert.NotNull(questorActor.Actor.Get<FactionComponent>().RegionalPeopleFactionId);

        // Hiding an NPC excludes it from QueryNearby.
        session.State.Npcs.SetPresence(civId, DaggerfallNpcPresence.Hidden);
        Assert.DoesNotContain(session.QueryNearby(origin, radius: 10d), hit => hit.Actor.DurableId == civId);
    }

    [Fact]
    public void Hud_escort_moves_to_WithParty_absent_from_QueryNearby_survives_save_reload_and_delivers_without_duplicate_body()
    {
        var definitions = EscortQuestDefinitions();
        using var fixture = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        DaggerfallSession session = fixture.Session;

        var started = session.State.Quests.Start(new("escort", "escort.txt", "escort", DaggerfallQuestLifecycle.Active, null, [], []));
        fixture.Update();
        Assert.True(session.State.Quests.TryGet(started.InstanceId, out var admitted));
        long escortId = admitted!.Resources.Single().Binding.ActorIds.Single();

        // Initially placed at the site: live in EntityStore and visible to QueryNearby.
        Assert.True(session.State.Actors.TryGet(escortId, out ActorState? placed));
        WorldPoint startPos = placed!.Position;
        Assert.Contains(session.QueryNearby(startPos, radius: 5d), hit => hit.Actor.DurableId == escortId);

        // Trigger AddFace -> escort moves to WithParty in ActorLedger and unloads from EntityStore.
        TriggerTask(session, started.InstanceId, "join");
        fixture.Update();

        Assert.True(session.State.Quests.IsEscortWithParty(escortId));
        Assert.False(session.State.Actors.TryGet(escortId, out _));
        Assert.DoesNotContain(session.QueryNearby(startPos, radius: 1000d), hit => hit.Actor.DurableId == escortId);
        Assert.True(session.State.ActorLedger.TryGet(escortId, out ActorLedgerEntry? ledgerEntry));
        Assert.Equal(ActorLocationKind.WithParty, ledgerEntry!.Location?.Kind);
        Assert.Equal(ActorPartyRole.Escort, ledgerEntry.Location?.PartyRole);

        // Save and reload while WithParty: still in ActorLedger as WithParty, absent from EntityStore and QueryNearby.
        RulesetSavePayload saveBytes = session.CaptureSave();
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(saveBytes);
        ActorLedgerEntry savedEscort = Assert.Single(saved.ActorLedger, entry => entry.DurableId == escortId);
        Assert.Equal(ActorLocationKind.WithParty, savedEscort.Location?.Kind);

        using DaggerfallSession restored = DaggerfallSession.Restore(fixture.Engine.Context, fixture.Composition, saveBytes);
        Assert.True(restored.State.Quests.IsEscortWithParty(escortId));
        Assert.False(restored.State.Actors.TryGet(escortId, out _));
        Assert.DoesNotContain(restored.QueryNearby(startPos, radius: 1000d), hit => hit.Actor.DurableId == escortId);

        // Deliver: DropFace + explicit placement at destination produces exactly ONE ActorState with the same durable ID.
        TriggerTask(restored, started.InstanceId, "deliver");
        restored.Update(new ProductUpdate(TestSessions.OuterUpdate(10), []));

        Assert.False(restored.State.Quests.IsEscortWithParty(escortId));
        ActorState delivered = Assert.Single(restored.State.Actors.All, actor => actor.DurableId == escortId);
        Assert.Single(restored.State.Npcs.All, npc => npc.DurableId == escortId);
        Assert.Contains(restored.QueryNearby(delivered.Position, radius: 5d), hit => hit.Actor.DurableId == escortId);
    }

    private static void TriggerTask(DaggerfallSession session, string instanceId, string taskSymbol)
    {
        DaggerfallQuestInstancesSave snapshot = session.State.Quests.Capture();
        session.State.Quests.Restore(snapshot with
        {
            Instances = snapshot.Instances.Select(instance => instance.InstanceId == instanceId
                ? instance with
                {
                    Tasks = instance.Tasks.Select(task => task.Symbol == taskSymbol
                        ? task with { IsSet = true }
                        : task).ToArray(),
                }
                : instance).ToArray(),
        });
    }

    private static DaggerfallDefinitions EscortQuestDefinitions()
    {
        JsonArray root = TestPayload.QuestRecords();
        root.Add(JsonNode.Parse("""
            {
              "name": "escort",
              "displayName": "",
              "sourceFile": "escort.txt",
              "messages": [],
              "blocks": [
                { "kind": "person", "firstLine": 1, "lines": ["Person _vip_ named Queen_Akorithi atHome female"] },
                { "kind": "task", "firstLine": 10, "lines": ["_join_ task:", "add _vip_ face"] },
                { "kind": "task", "firstLine": 20, "lines": ["_deliver_ task:", "drop _vip_ face", "place npc _vip_ at _vip_.home"] }
              ]
            }
            """)!);
        return TestPayload.WithQuests(root);
    }
}
