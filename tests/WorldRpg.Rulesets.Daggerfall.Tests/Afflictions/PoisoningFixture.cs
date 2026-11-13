using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Poisons built without the session: the player and one site actor, both with the player's attributes, the poison
/// effects over a lifecycle, the vitality owner their health arms act through, item instances for a coating, and a
/// draw that answers the top of every window.
/// </summary>
internal sealed class PoisoningFixture : IDisposable
{
    internal const long SiteActor = 3;

    internal PoisoningFixture(int? siteActorLevel = 5, long draws = 0)
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallActorDefinition player = definitions.RequireActor(new DaggerfallActorId("player"));
        DaggerfallCareerDefinition career = definitions.Catalogs.RequireCareer("class00");
        StatsComponent playerStats = Attributed(definitions, player);
        Actors.CreatePlayer(DaggerfallActorIdentity.PlayerEntityId, new EntityTypeId("player"), playerStats, "health", DaggerActorFactory.PlayerCapabilities);
        Actors.CreateActor(SiteActor, new EntityTypeId("nymph"), Attributed(definitions, player),
            new ActorPose(new WorldPoint(1f, 0f, 0f), 0f), "health", DaggerActorFactory.NonPlayerCapabilities);
        Character = new DaggerfallCharacterState(definitions, playerStats, player);
        Dictionary<long, DaggerfallActorDefinition> actorDefinitions = [];
        if (siteActorLevel is int level)
            actorDefinitions[SiteActor] = definitions.RequireActor(new DaggerfallActorId("nymph")) with { Level = level };
        IRandomService random = RandomMaximum.Create();
        Effects = new(Actors, new DaggerfallEffectCatalog(DaggerfallPoisonEffects.Definitions(
            random, Vitality, () => career)));
        Poisoning = new DaggerfallPoisoning(Effects, random, Actors, Character, Items, actorDefinitions, definitions,
            new CombatResolution(), draws);
    }

    private ulong _lastWeapon = 76;

    internal ActorsState Actors { get; } = new();
    internal DaggerfallVitalityConsequences Vitality { get; } = new(new CombatResolution());
    internal DaggerfallEffectLifecycle Effects { get; }
    internal DaggerfallCharacterState Character { get; }
    internal DaggerfallItemInstances Items { get; } = new();
    internal DaggerfallPoisoning Poisoning { get; }
    internal Actor Player => Actors.Player.Actor;

    internal Actor Site => Actors.TryGet(SiteActor, out ActorState actor) ? actor.Actor : throw new InvalidOperationException();

    /// <summary>A unique weapon carrying a coating of <paramref name="variant"/>.</summary>
    internal ulong CoatedWeapon(int variant)
    {
        ulong weapon = ++_lastWeapon;
        Items.RegisterUnique(weapon, new DaggerfallItemInstanceMetadata("iron-dagger", "iron", 0, 8, 8, Identified: true,
            Stolen: false, QuestId: null, QuestItemSymbol: null, Enchantment: null, DaggerfallItemOwner.Player) { PoisonVariant = variant });
        return weapon;
    }

    public void Dispose() => Actors.Dispose();

    private static StatsComponent Attributed(DaggerfallDefinitions definitions, DaggerfallActorDefinition player) =>
        new DaggerfallMechanicsState().CreateStats(player,
            DaggerfallPlayerVitals.Initial(player.Stats, definitions.Catalogs.RequireCareer("class00")));
}
