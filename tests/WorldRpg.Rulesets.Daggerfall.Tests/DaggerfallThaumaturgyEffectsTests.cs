using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallThaumaturgyEffectsTests
{
    [Fact]
    public void Spatial_effects_grant_and_remove_typed_support_and_compile_donor_bindings()
    {
        DaggerfallEffectDefinition[] definitions = DaggerfallThaumaturgyEffects.Definitions().ToArray();
        Assert.Equal((14, -1), Binding(definitions, "levitate"));
        Assert.Equal((31, -1), Binding(definitions, "water-walking"));
        Assert.Equal((21, -1), Binding(definitions, "spell-reflection"));
        Assert.Equal((22, -1), Binding(definitions, "spell-resistance"));

        using ActorsState actors = Actors();
        using DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog(definitions));
        Start(effects, "levitate", 14, -1, target: 1);
        Start(effects, "water-walking", 31, -1, target: 2);

        Assert.True(effects.GrantsLevitation(1));
        Assert.False(effects.GrantsLevitation(2));
        Assert.True(effects.GrantsWaterWalking(2));
        Assert.False(effects.GrantsWaterWalking(1));

        Assert.True(effects.Cancel(EffectInstanceId.Parse("water-walking.instance")));
        Assert.False(effects.GrantsWaterWalking(2));
        Assert.True(effects.Cancel(EffectInstanceId.Parse("levitate.instance")));
        Assert.False(effects.GrantsLevitation(1));
    }

    [Fact]
    public void Reflection_and_resistance_are_live_defenses_and_restore_with_duration_state()
    {
        DaggerfallEffectDefinition[] definitions = DaggerfallThaumaturgyEffects.Definitions().ToArray();
        using ActorsState actors = Actors();
        using DaggerfallEffectLifecycle effects = new(actors, new DaggerfallEffectCatalog(definitions));
        Start(effects, "spell-reflection", 21, -1, target: 2, chance: 100, rounds: 2);
        Start(effects, "spell-resistance", 22, -1, target: 2, chance: 100, rounds: 2);

        DaggerfallMagicDefense defense = effects.MagicDefenseFor(2);
        Assert.Equal(100, defense.ReflectionChance);
        Assert.Equal(100, defense.AllResistanceChance);

        DaggerfallActiveEffectSave[] saved = effects.Capture();
        using ActorsState restoredActors = Actors();
        using DaggerfallEffectLifecycle restored = new(restoredActors, new DaggerfallEffectCatalog(definitions));
        restored.Restore(saved);
        Assert.Equal(100, restored.MagicDefenseFor(2).ReflectionChance);
        Assert.Equal(100, restored.MagicDefenseFor(2).AllResistanceChance);

        Assert.Equal(2u, restored.AdvanceElapsedRounds(2));
        Assert.Empty(restored.Active);
    }

    private static (int Type, int SubType) Binding(IReadOnlyList<DaggerfallEffectDefinition> definitions, string key)
    {
        DaggerfallSpellBinding binding = definitions.Single(definition => definition.Key == key).Spell
            ?? throw new InvalidOperationException($"Effect '{key}' has no compiled spell binding.");
        return (binding.Type, binding.SubType);
    }

    private static void Start(DaggerfallEffectLifecycle effects, string key, int type, int subtype,
        long target, int chance = 100, uint rounds = 5)
    {
        DaggerfallSpellEffectDefinition setting = new(key, type, subtype, 20, 0, 1, chance, 0, 1, 0, 0, 0, 0, 0);
        JsonElement state = JsonSerializer.SerializeToElement(
            new DaggerfallCastEffectState(setting, 1, 0, 100),
            DaggerfallSaveJsonContext.Default.DaggerfallCastEffectState);
        DaggerfallEffectAdmissionOutcome outcome = effects.Start(new(
            $"{key}.instance", key, "spell.test", 1, target, key, "Magic", null, 1, rounds, state));
        Assert.True(outcome is DaggerfallEffectAdmissionOutcome.Started or DaggerfallEffectAdmissionOutcome.Refreshed);
    }

    private static ActorsState Actors()
    {
        ActorsState actors = new();
        actors.CreatePlayer(1, new EntityTypeId("player"), Stats(), "health", DaggerActorFactory.PlayerCapabilities);
        actors.CreateActor(2, new EntityTypeId("target"), Stats(), new ActorPose(new WorldPoint(0, 0, 0), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
        return actors;
    }

    private static StatsComponent Stats()
    {
        Stat maximum = new(100);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse("health-maximum"), maximum);
        stats.AddTrack(TrackId.Parse("health"), new Track(maximum, 100));
        return stats;
    }
}
