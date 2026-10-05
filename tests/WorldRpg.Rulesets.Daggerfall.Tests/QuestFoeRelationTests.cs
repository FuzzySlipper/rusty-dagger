using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Combat;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestFoeRelationTests
{
    [Theory]
    [InlineData("change foe _enemy_ team PlayerAlly", 1)]
    [InlineData("change foe _enemy_ team 21", 21)]
    [InlineData("change foe _enemy_ team 99", 99)]
    [InlineData("change foe _enemy_ infighting true", 1)]
    [InlineData("change foe _enemy_ infighting false", 0)]
    [InlineData("restrain foe _enemy_", 1)]
    [InlineData("unrestrain foe _enemy_", 0)]
    [InlineData("enemies makehostile", 0)]
    [InlineData("enemies clear", 1)]
    public void Named_numeric_and_command_forms_compile(string action, int value)
    {
        var source = new DaggerfallQuestSourceDefinition("relations", "", "relations.txt", DaggerfallQuestDisposition.Compiled, [], [new("headless", 1, [action], null)], []);
        Assert.Equal(value, Assert.Single(Assert.Single(DaggerfallQuestTaskCompiler.Compile(source).Tasks).Operations).Step);
    }

    [Fact]
    public void Restraint_team_and_infighting_restore_and_cleanup_without_changing_base_actor()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["create foe _enemy_ every 0 minutes 1 times with 100% success", "change foe _enemy_ team PlayerAlly", "change foe _enemy_ infighting true", "restrain foe _enemy_"],
            taskBlocks: [["_free_ task:", "clicked foe _enemy_", "unrestrain foe _enemy_"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session);
        long id = Foe(f.Session);
        string? original = f.Session.DefinitionsByActor[id].Team;
        Assert.Equal("player-ally", f.Session.EffectiveFoeTeam(id));
        Assert.True(f.Session.State.Quests.IsFoeRestrained(id));
        Assert.False(f.Session.State.Quests.IsFoeRestrained(f.Enemy));
        f.Update();
        Assert.Equal(EnemyBehaviorState.Idle, f.Session.LastEnemyBehavior[id].State);
        Assert.True(f.Session.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        using var restored = f.Restore();
        Assert.Equal("player-ally", restored.EffectiveFoeTeam(id));
        Assert.True(restored.State.Quests.AllowsFoeInfighting(id));
        Assert.True(restored.State.Quests.IsFoeRestrained(id));
        Assert.True(restored.State.Quests.ActorClicked(id));
        Advance(restored);
        Assert.False(restored.State.Quests.IsFoeRestrained(id));
        restored.State.Quests.Complete("relations", "success");
        Assert.Equal(original, restored.EffectiveFoeTeam(id));
        Assert.Equal(original, restored.DefinitionsByActor[id].Team);
        Assert.True(restored.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
    }

    [Fact]
    public void Player_ally_uses_existing_pursuit_only_when_quest_allows_ai_fighting()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["create foe _enemy_ every 0 minutes 1 times with 100% success", "change foe _enemy_ team 1", "change foe _enemy_ infighting true"],
            taskBlocks: [["_quiet_ task:", "clicked foe _enemy_", "change foe _enemy_ infighting false"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session);
        long id = Foe(f.Session);
        f.Session.State.Actors.Get(id).ApplyPose(new(new(0, 0, -4), 0));
        f.Perception.Responder = request => Receipt(request.Observers.ToArray().SelectMany(o => request.Targets.ToArray()
            .Select(t => new PerceptionPair(o.Entity, t.Entity, Vector3.Distance(o.Origin, t.Center), 1, PerceptionPairKind.Visible, 1))).ToArray());
        f.Update();
        Assert.Equal((ulong)f.Enemy, Assert.Single(f.Session.LastEnemyBehavior[id].Visibility!.Value.Pairs.ToArray()).Target);
        Assert.True(f.Session.State.Quests.ActorClicked(id)); Advance(f.Session); f.Update();
        Assert.Equal(EnemyBehaviorState.Idle, f.Session.LastEnemyBehavior[id].State);
        Assert.Null(f.Session.LastEnemyBehavior[id].Visibility);
    }

    [Fact]
    public void Enemies_clear_removes_real_actors_and_does_not_respawn_them_after_save()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["enemies clear"]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session);
        Assert.DoesNotContain(f.Session.DefinitionsByActor.Values, value => value.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass);
        using var restored = f.Restore();
        Assert.DoesNotContain(restored.DefinitionsByActor.Values, value => value.Kind is DaggerfallActorKinds.Monster or DaggerfallActorKinds.EnemyClass);
        Assert.False(restored.State.Actors.Player.IsDefeated);
    }

    [Fact]
    public void Accepted_player_strike_breaks_only_that_foes_restraint_and_end_releases_the_rest()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["create foe _enemy_ every 0 minutes 1 times with 100% success", "restrain foe _enemy_"], foeCount: 2);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session); Advance(f.Session);
        long[] ids = f.Session.State.Quests.Capture().Instances.Single().Resources.Single(r => r.SelectedFoe is not null).Binding.ActorIds;
        Assert.Equal(2, ids.Length);
        Assert.All(ids, id => Assert.True(f.Session.State.Quests.IsFoeRestrained(id)));
        f.Session.State.Kit.Rules.RegisterAction(f.Session.DefinitionsByActor[1].ActionId!, new OneDamage());
        f.Session.ResolveExplicitMelee(new(1, ids[0], 1, 10000, .125));
        Assert.False(f.Session.State.Quests.IsFoeRestrained(ids[0]));
        Assert.True(f.Session.State.Quests.IsFoeRestrained(ids[1]));
        using var restored = f.Restore();
        Assert.False(restored.State.Quests.IsFoeRestrained(ids[0]));
        Assert.True(restored.State.Quests.IsFoeRestrained(ids[1]));
        restored.State.Quests.Complete("relations", "done");
        Assert.All(ids, id => Assert.False(restored.State.Quests.IsFoeRestrained(id)));
    }

    [Fact]
    public void Team_and_infighting_commands_affect_current_actors_while_restraint_covers_later_admission()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["create foe _enemy_ every 0 minutes 1 times with 100% success", "change foe _enemy_ team PlayerAlly", "change foe _enemy_ infighting true", "restrain foe _enemy_"], foeCount: 2);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session);
        long first = Foe(f.Session);
        Advance(f.Session);
        long second = f.Session.State.Quests.Capture().Instances.Single().Resources.Single(r => r.SelectedFoe is not null).Binding.ActorIds.Single(id => id != first);
        Assert.Equal("player-ally", f.Session.EffectiveFoeTeam(first));
        Assert.True(f.Session.State.Quests.AllowsFoeInfighting(first));
        Assert.Equal(f.Session.DefinitionsByActor[second].Team, f.Session.EffectiveFoeTeam(second));
        Assert.False(f.Session.State.Quests.AllowsFoeInfighting(second));
        Assert.True(f.Session.State.Quests.IsFoeRestrained(second));
        using var restored = f.Restore();
        Assert.Equal("player-ally", restored.EffectiveFoeTeam(first));
        Assert.Equal(restored.DefinitionsByActor[second].Team, restored.EffectiveFoeTeam(second));
    }

    [Fact]
    public void Global_hostility_updates_allied_foes_base_disposition_before_team_cleanup()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["create foe _enemy_ every 0 minutes 1 times with 100% success", "change foe _enemy_ team PlayerAlly"],
            taskBlocks: [["_hostile_ task:", "clicked foe _enemy_", "enemies makehostile"]]);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions);
        Start(f, definitions); Advance(f.Session);
        long id = Foe(f.Session);
        f.Session.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(false);
        Assert.True(f.Session.State.Quests.ActorClicked(id)); Advance(f.Session);
        Assert.True(f.Session.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
        using var restored = f.Restore();
        restored.State.Quests.Complete("relations", "done");
        Assert.NotEqual("player-ally", restored.EffectiveFoeTeam(id));
        Assert.True(restored.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>().ForcedHostile);
    }

    private sealed class OneDamage : ICombatContribution
    {
        public void Hit(TryHitEvent value) => value.Hit = true;
        public void Damage(DamageEvent value) => value.Damage = 1;
    }

    private static long Foe(DaggerfallSession s) => s.State.Quests.Capture().Instances.Single().Resources.Single(r => r.SelectedFoe is not null).Binding.ActorIds.Single();
    private static void Advance(DaggerfallSession s) => s.State.Quests.Advance(s.State.Variables, DaggerfallCalendar.Start);
    private static void Start(SanguineRoseSessionTests.Fixture f, DaggerfallDefinitions definitions)
    {
        var site = definitions.Locations.Records.Single(value => value.Id == f.Inputs.Site);
        f.Session.State.Quests.Start(new("relations", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId) })], []));
    }
}
