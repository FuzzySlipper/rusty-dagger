using System.Text.Json;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallEffectMovementProtectionTests
{
    [Fact]
    public void Active_compiled_effect_exposes_fall_protection_by_target_until_it_ends()
    {
        using ActorsState actors = new();
        actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), Stats(), "health");
        DaggerfallEffectCatalog catalog = new(
        [
            new DaggerfallEffectDefinition("feather-fall", "feather-fall", DaggerfallEffectStacking.Stack, 1, 1,
                MovementProtection: new DaggerfallMovementProtection(PreventsFallDamage: true)),
        ]);
        using DaggerfallEffectLifecycle effects = new(actors, catalog);
        DaggerfallEffectRequest request = new("fall-protection", "feather-fall", "spell", null,
            DaggerfallActorIdentity.PlayerEntityId, "test", null, null, 1, 2, EmptyState());

        _ = effects.Start(request);
        Assert.True(effects.PreventsFallDamage(DaggerfallActorIdentity.PlayerEntityId));
        Assert.False(effects.PreventsFallDamage(2));

        Assert.True(effects.Cancel(EffectInstanceId.Parse("fall-protection")));
        Assert.False(effects.PreventsFallDamage(DaggerfallActorIdentity.PlayerEntityId));
    }

    private static StatsComponent Stats()
    {
        Stat maximum = new(100);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), maximum);
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value), new Track(maximum, 100, 0));
        return stats;
    }

    private static JsonElement EmptyState()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }
}
