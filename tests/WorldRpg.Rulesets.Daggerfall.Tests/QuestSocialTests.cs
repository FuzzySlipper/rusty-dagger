using Rusty.Engine;
using System.Numerics;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestSocialTests
{
    [Theory]
    [InlineData("change repute with _person_ by +10")]
    [InlineData("change repute with _person_ by -20")]
    [InlineData("legal repute -25")]
    [InlineData("repute with _person_ exceeds 10 do _done_")]
    [InlineData("when repute with Akorithi is at least 10")]
    [InlineData("setplayercrime High_Treason")]
    [InlineData("setplayercrime None")]
    public void Source_forms_compile(string action)
    {
        var source = new DaggerfallQuestSourceDefinition("social", "", "social.txt", DaggerfallQuestDisposition.Compiled, [], [new("headless", 1, [action], null)], []);
        Assert.Empty(DaggerfallQuestTaskCompiler.Assess(source));
    }

    [Fact]
    public void Selected_person_reputation_propagates_and_legal_clamps_through_shared_saved_social_owner()
    {
        var defs = QuestWorldAdmissionTests.Definitions(person: true, personQuestor: true,
            actions: ["change repute with _person_ by +20", "legal repute +200", "repute with _person_ exceeds 20 do _done_"], taskBlocks: [["_done_ task:"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: defs);
        long npc = QuestNpcOverlayTests.Giver(f, defs, 66);
        var expected = new DaggerfallSocialState(defs.Factions); expected.Restore(f.Session.State.Social.Capture());
        expected.ChangeFactionReputation(66, 20, DaggerfallFactionReputationChange.Propagate);
        Start(f, npc); Advance(f.Session);
        Assert.Equal(expected.Capture().Factions, f.Session.State.Social.Capture().Factions);
        Assert.Equal(100, f.Session.State.Social.RegionalReputation(f.Session.Site.Region!.Value));
        Assert.True(Quest(f.Session).Tasks.Single(t => t.Symbol == "done").IsSet);
        Assert.Equal(f.Session.State.Social.ReactionForFaction(66), f.Session.State.Social.ReactionForNpc(f.Session.State.Npcs.Require(npc)));
        using var restored = f.Restore(); Advance(restored);
        Assert.Equal(expected.Capture().Factions, restored.State.Social.Capture().Factions);
        Assert.Equal(100, restored.State.Social.RegionalReputation(restored.Site.Region!.Value));
    }

    [Fact]
    public void Named_individual_threshold_is_always_on_but_actions_repeat_only_after_explicit_rearm()
    {
        var individual = TestPayload.Definitions.QuestSources.Tables.ActorItemTables.Factions.Rows.First(row => row.Active
            && TestPayload.Definitions.Factions.Factions.TryGetValue(row.P3, out var faction) && faction.Type == 4);
        var defs = QuestWorldAdmissionTests.Definitions(taskBlocks: [
            ["_watch_ task:", $"when repute with {individual.Name} is at least 50", "legal repute +1"],
            ["_rearm_ task:", "clear _watch_", "clear _rearm_"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: defs); Start(f); Advance(f.Session);
        int region = f.Session.Site.Region!.Value;
        int before = f.Session.State.Social.RegionalReputation(region);
        Assert.False(Quest(f.Session).Tasks.Single(t => t.Symbol == "watch").IsSet);
        f.Session.State.Social.ChangeFactionReputation(individual.P3, 50 - f.Session.State.Social.FactionReputation(individual.P3));
        Advance(f.Session); Assert.Equal(before + 1, f.Session.State.Social.RegionalReputation(region));
        f.Session.State.Social.ChangeFactionReputation(individual.P3, -1); Advance(f.Session);
        Assert.False(Quest(f.Session).Tasks.Single(t => t.Symbol == "watch").IsSet);
        f.Session.State.Social.ChangeFactionReputation(individual.P3, 1); Advance(f.Session);
        Assert.Equal(before + 1, f.Session.State.Social.RegionalReputation(region));
        SetTask(f.Session, "rearm"); Advance(f.Session);
        using var restored = f.Restore(); Advance(restored);
        Assert.Equal(before + 2, restored.State.Social.RegionalReputation(region));
    }

    [Theory]
    [InlineData(true, 66)]
    [InlineData(false, 66)]
    [InlineData(true, 0)]
    public void Terminal_settlement_waits_for_final_reward_and_persists_exactly_once(bool reward, int faction)
    {
        var defs = QuestWorldAdmissionTests.Definitions(actions: ["start task _final_", "end quest"],
            taskBlocks: [reward ? ["_final_ task:", "give pc _gift_"] : ["_final_ task:"]], messages: ["Quest complete."], firstMessageId: 1004);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: defs);
        var expected = new DaggerfallSocialState(defs.Factions); expected.Restore(f.Session.State.Social.Capture());
        if (faction != 0) expected.ChangeFactionReputation(faction, reward ? 5 : -2, DaggerfallFactionReputationChange.Propagate);
        Start(f, faction: faction); Advance(f.Session);
        Assert.False(Quest(f.Session).FactionSettled);
        using var restored = f.Restore(); Advance(restored); Advance(restored);
        Assert.Equal(reward, Quest(restored).Succeeded);
        Assert.True(Quest(restored).FactionSettled);
        Assert.Equal(expected.Capture().Factions, restored.State.Social.Capture().Factions);
        var restoredSave = restored.CaptureSave();
        using var again = DaggerfallSession.Restore(f.Engine.Context, f.Composition, restoredSave); Advance(again);
        Assert.Equal(expected.Capture().Factions, again.State.Social.Capture().Factions);
    }

    [Fact]
    public void Missing_person_and_unknown_individual_or_crime_are_diagnostic_without_later_mutation()
    {
        var defs = QuestWorldAdmissionTests.Definitions(actions: ["change repute with _missing_ by +1", "legal repute -100"],
            taskBlocks: [["_unknown_ task:", "when repute with MissingPerson is at least 1"], ["_crime_ task:", "setplayercrime Unknown"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: defs); Start(f); SetTask(f.Session, "crime"); Advance(f.Session);
        Assert.NotNull(Quest(f.Session).Tasks.First().OperationState[0].UnavailableReason);
        Assert.NotNull(Quest(f.Session).Tasks.Single(t => t.Symbol == "unknown").OperationState[0].UnavailableReason);
        Assert.NotNull(Quest(f.Session).Tasks.Single(t => t.Symbol == "crime").OperationState[0].UnavailableReason);
        Assert.Equal(0, f.Session.State.Social.RegionalReputation(f.Session.Site.Region!.Value));
        using var restored = f.Restore(); Assert.Null(restored.State.Crime.ScriptedCrime);
    }

    [Fact]
    public void Scripted_charge_waits_for_real_watch_damage_then_uses_canonical_legal_response_once()
    {
        var defs = QuestWorldAdmissionTests.Definitions(actions: ["setplayercrime High_Treason"], taskBlocks: [["_hostile_ task:", "enemies makehostile"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: defs); Start(f); Advance(f.Session);
        Assert.Equal(DaggerfallCrimeKind.HighTreason, f.Session.State.Crime.ScriptedCrime);
        Assert.Empty(f.Session.State.Crime.Incidents);
        using var restored = f.Restore();
        Assert.Equal(DaggerfallCrimeKind.HighTreason, restored.State.Crime.ScriptedCrime);
        var session = f.Session;
        var player = session.State.PlayerControl.Position!.Value;
        long guard = session.SpawnActor(defs.Actors.Values.Single(a => a.MobileId == 146).Id.Value, new(new(player.X, player.Y, player.Z - 1), MathF.PI));
        session.State.Kit.Rules.RegisterAction(session.DefinitionsByActor[guard].ActionId!, new ForceHit());
        SetTask(session, "hostile"); Advance(session);
        f.Perception.Responder = request => Receipt(request.Observers.ToArray().SelectMany(observer => request.Targets.ToArray()
            .Select(target => new PerceptionPair(observer.Entity, target.Entity, Vector3.Distance(observer.Origin, target.Center), 1, PerceptionPairKind.Visible, 1))).ToArray());
        f.Update();
        ulong marker = checked((ulong)f.Inputs.MobileSprites[146].AttackSequences[0].SourceFrames.ToList().IndexOf(-1) + 1);
        f.Appearance.AdvanceReceiptForAll = CrossedMarker(1, markerId: marker);
        for (int step = 0; step < 10 && session.State.Crime.ScriptedCrime is not null; step++)
        {
            var current = session.State.PlayerControl.Position!.Value;
            session.State.Actors.Get(guard).ApplyPose(new(new(current.X, current.Y, current.Z - .1f), MathF.PI));
            f.Update();
        }
        Assert.True(session.State.Crime.ScriptedCrime is null, $"Guard: {session.LastEnemyBehavior.GetValueOrDefault(guard)}; pending: {session.State.Actors.Get(guard).Attack.Pending}; health: {session.State.Actors.Player.Stats.GetTrack(Rusty.Engine.Mechanics.TrackId.Parse("health")).Current}");
        var incident = Assert.Single(session.State.Crime.Incidents);
        Assert.Equal(DaggerfallCrimeKind.HighTreason, incident.Crime);
        Assert.Equal(DaggerfallCrimeWitnessQuery.NotQueried, incident.Witnesses.Query);
        Assert.Contains(incident.OperationId, Assert.Single(session.State.Crime.LegalResponses).Charges);
        f.Update(); Assert.Single(session.State.Crime.Incidents);

    }

    [Fact]
    public void Unknown_faction_is_rejected_at_admission_and_restore_before_any_state_changes()
    {
        var defs = QuestWorldAdmissionTests.Definitions();
        using var f = new SanguineRoseSessionTests.Fixture(definitions: defs);
        Assert.Throws<ArgumentException>(() => Start(f, faction: int.MaxValue));
        Assert.Empty(f.Session.State.Quests.Capture().Instances);
        Start(f);
        var saved = f.Session.State.Quests.Capture();
        Assert.Throws<ArgumentException>(() => f.Session.State.Quests.Restore(saved with { Instances = [saved.Instances.Single() with { FactionId = int.MaxValue }] }));
        Assert.Throws<ArgumentException>(() => f.Session.State.Quests.Restore(saved with { PendingStarts = [new("pending", "world-test.txt", null, int.MaxValue)] }));
        Assert.Equal(0, Quest(f.Session).FactionId);
    }

    private sealed class ForceHit : ICombatContribution
    { public void Hit(TryHitEvent value) => value.Hit = true; public void Damage(DamageEvent value) => value.Damage = 1; }
    private static DaggerfallQuestInstanceSave Quest(DaggerfallSession s) => s.State.Quests.Capture().Instances.Single();
    private static void Advance(DaggerfallSession s) => s.State.Quests.Advance(s.State.Variables, DaggerfallCalendar.Start);
    private static void SetTask(DaggerfallSession s, string name)
    {
        var saved = s.State.Quests.Capture();
        s.State.Quests.Restore(saved with { Instances = saved.Instances.Select(q => q with { Tasks = q.Tasks.Select(t => t.Symbol == name ? t with { IsSet = true } : t).ToArray() }).ToArray() });
    }
    private static void Start(SanguineRoseSessionTests.Fixture f, long? npc = null, int faction = 0)
    {
        var site = TestPayload.Definitions.Locations.Records.Single(value => value.Id == f.Inputs.Site);
        f.Session.State.Quests.Start(new("social", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId) })], [])
            { QuestorId = npc, FactionId = faction });
    }
}
