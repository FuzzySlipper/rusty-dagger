using System.Reflection;
using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The construction a combat fact needs, in one place: a player and one opposing actor in an actor state,
/// the combat rules over them, and a fact buffer. A fact that cares about one rule — fatigue after an
/// accepted hit, say — starts here and asserts what it is about, so a change to what combat needs lands
/// in this file rather than in every fact that builds its own.
/// </summary>
/// <remarks>
/// Two of the rules' dependencies need host-composed services, so this fixture cannot build them: the
/// targeting service takes perception and spatial movement, and the equipment coordinator takes a composed
/// inventory. They are passed as null here, in one place with this note, so a rule that starts needing one
/// fails in front of whoever added the dependency instead of inside an unrelated fact.
/// </remarks>
internal sealed class DaggerCombatFixture : IDisposable
{
    internal DaggerCombatFixture(string sourceId, double playerHealth = 100d, double playerStamina = 600d)
    {
        Definitions = TestPayload.Definitions;
        Actors = new ActorsState();
        Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), Stats(playerHealth, playerStamina), "health");
        Actors.CreateActor(2, new EntityTypeId(sourceId), Stats(100d, 600d), new ActorPose(new WorldPoint(1f, 0f, 0f), 0f), "health");
        Random = DispatchProxy.Create<IRandomService, MinimumRandomProxy>();
        Rules = new DaggerCombatRules(
            Random, Actors, null!, _ => null,
            new DaggerfallItemInstances(), Definitions,
            new Dictionary<long, DaggerfallActorDefinition>
            {
                [DaggerfallActorIdentity.PlayerEntityId] = Definitions.RequireActor(new DaggerfallActorId("player")),
                [2] = Definitions.RequireActor(new DaggerfallActorId(sourceId)),
            },
            null!);
    }

    internal DaggerfallDefinitions Definitions { get; }

    internal ActorsState Actors { get; }

    /// <summary>The fixture's random service, which answers the minimum of every keyed window.</summary>
    internal IRandomService Random { get; }

    internal DaggerCombatRules Rules { get; }

    internal FactBuffer<IProductFact> Facts { get; } = new();

    internal double Health => Track(DaggerfallMechanicsIds.Health.Value);

    internal double Stamina => Track(DaggerfallMechanicsIds.Stamina.Value);

    internal List<IProductFact> Deliver()
    {
        List<IProductFact> delivered = [];
        Facts.Deliver(delivered.Add);
        return delivered;
    }

    internal static StatsComponent Stats(double health, double stamina)
    {
        Stat healthMaximum = new(1_000);
        Stat staminaMaximum = new(1_000);
        StatsComponent stats = new();
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.HealthMaximum.Value), healthMaximum);
        stats.AddStat(StatId.Parse(DaggerfallMechanicsIds.StaminaMaximum.Value), staminaMaximum);
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Health.Value), new Track(healthMaximum, health, 0d));
        stats.AddTrack(TrackId.Parse(DaggerfallMechanicsIds.Stamina.Value), new Track(staminaMaximum, stamina, 0d));
        return stats;
    }

    public void Dispose() => Actors.Dispose();

    private double Track(string id) => Actors.Player.Stats.GetTrack(TrackId.Parse(id)).Current;

    private class MinimumRandomProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
            ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum)
            : throw new NotSupportedException(method?.Name);
    }
}
