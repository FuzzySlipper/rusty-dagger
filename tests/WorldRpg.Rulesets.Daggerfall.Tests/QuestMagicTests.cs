using System.Numerics;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.QuestDiseaseTests;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestMagicTests
{
    [Theory]
    [InlineData("cast Levitate spell do done")]
    [InlineData("cast Levitate effect do done")]
    public void Watcher_requires_real_successful_delivery_and_retains_observation_across_reload(string action)
    {
        using var f = new SanguineRoseSessionTests.Fixture(definitions: Definitions(action));
        var s = f.Session;
        Start(s); Advance(s);
        string spell = TestPayload.Definitions.Magic.Spells.Values.First(value => !value.IsCustom && value.Identity == 4).Key;
        s.State.Character.LearnSpell(spell);
        var mana = s.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); mana.Maximum.BaseValue = 10000; mana.SetCurrent(0);
        Assert.Equal(DaggerfallCastOutcome.InsufficientMagicka, s.ReadyPlayerSpell(spell).Outcome);
        Advance(s); Assert.False(Assert.Single(s.State.Quests.All).Tasks[1].IsSet);
        mana.SetCurrent(10000);
        Assert.Equal(DaggerfallCastOutcome.Ready, s.ReadyPlayerSpell(spell).Outcome);
        Advance(s); Assert.False(Assert.Single(s.State.Quests.All).Tasks[1].IsSet);
        Assert.Equal(DaggerfallCastOutcome.DeliveryCompleted, s.ReleaseReadySpell(1, Vector3.UnitZ).Outcome);
        Assert.NotNull(Assert.Single(s.State.Quests.All).Tasks[0].OperationState[0].ObservedCastSequence);
        using var restored = f.Restore(); Advance(restored); Advance(restored);
        var quest = Assert.Single(restored.State.Quests.All);
        Assert.True(quest.Tasks[0].OperationCompleted[0]);
        Assert.True(quest.Tasks[1].IsSet);
        Assert.Single(restored.State.Effects.Active, effect => effect.Definition.Key == "levitate");
    }

    [Theory]
    [InlineData("cast missing spell do done")]
    [InlineData("cast missing effect do done")]
    public void Unsupported_reference_does_not_complete_or_start_a_branch(string action)
    {
        using var f = new SanguineRoseSessionTests.Fixture(definitions: Definitions(action));
        Start(f.Session); Advance(f.Session);
        var quest = Assert.Single(f.Session.State.Quests.All);
        Assert.False(quest.Tasks[0].OperationCompleted[0]); Assert.False(quest.Tasks[1].IsSet);
        Assert.Contains("missing", quest.Tasks[0].OperationState[0].UnavailableReason);
        using var restored = f.Restore(); Advance(restored);
        Assert.False(Assert.Single(restored.State.Quests.All).Tasks[1].IsSet);
    }

    [Fact]
    public void Foe_spell_queued_before_placement_delivers_once_to_each_actor_and_survives_reload()
    {
        var definitions = QuestWorldAdmissionTests.Definitions(actions: ["cast Shield spell on _enemy_", "place foe _enemy_ at _location_"], foeCount: 2);
        using var f = new SanguineRoseSessionTests.Fixture(definitions: definitions, prepareInputs: QuestWorldAdmissionTests.WithMarker);
        var s = f.Session;
        var site = definitions.Locations.Records.Single(value => value.Id == f.Inputs.Site);
        s.State.Quests.Start(new("foe-magic", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
            [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(f.Inputs.ProfileKind, site.MapId) })], []));
        Advance(s);
        var pending = Assert.Single(s.State.Quests.All);
        Assert.False(pending.Tasks[0].OperationCompleted[0]);
        Assert.Single(pending.Placements);
        using var restored = f.Restore();
        restored.State.Quests.AdmitPlacements(f.Inputs, restored);
        Advance(restored); Advance(restored);
        var quest = Assert.Single(restored.State.Quests.All);
        var foe = quest.Resources.Single(value => value.Symbol == "enemy");
        Assert.Equal(2, foe.Binding.ActorIds.Length);
        Assert.Equal(foe.Binding.ActorIds.Order(), Assert.Single(foe.FoeSpells).DeliveredActors.Order());
        Assert.True(quest.Tasks[0].OperationCompleted[0]);
        Assert.Equal(2, restored.State.Effects.Active.Count(effect => effect.Definition.Key == "shield"));
        long next = restored.Casting.NextSequence;
        Advance(restored);
        Assert.Equal(next, restored.Casting.NextSequence);
    }
}
