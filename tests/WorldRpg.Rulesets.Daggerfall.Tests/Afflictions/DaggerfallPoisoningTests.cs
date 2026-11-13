using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Who a poison reaches, built without the session: the player's exposure, a weapon's coating on a site actor,
/// drugs, and the admitted draw's saved ordinal.
/// </summary>
public sealed class DaggerfallPoisoningTests
{
    private const long SiteActor = 3;

    private sealed class Fixture : IDisposable
    {
        internal Fixture(int? siteActorLevel = 5, long draws = 0)
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
            DaggerfallEffectLifecycle effects = new(Actors, new DaggerfallEffectCatalog(DaggerfallPoisonEffects.Definitions(
                random, new DaggerfallVitalityConsequences(new CombatResolution()), () => career)));
            Poisoning = new DaggerfallPoisoning(effects, random, Actors, Character, Items, actorDefinitions, definitions,
                new CombatResolution(), draws);
        }

        private ulong _lastWeapon = 76;

        internal ActorsState Actors { get; } = new();
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

    [Fact]
    public void A_drug_reaches_the_player_past_the_first_level_and_is_cured_by_the_player_cure()
    {
        using Fixture fixture = new();
        DaggerfallPoisonExposure dose = fixture.Poisoning.PlayerExposure(bypassResistance: true);
        Assert.Equal(DaggerfallActorIdentity.PlayerEntityId, dose.TargetId);
        Assert.Equal(1, dose.TargetLevel);
        Assert.Equal(DaggerfallPoisonPolicy.CareerTolerance(fixture.Character.Career), dose.Tolerance);
        Assert.Equal(fixture.Character.Race.Tolerance(DaggerfallCareerTolerances.Poison), dose.RaceTolerance);

        // The donor refuses a first-level target; a bypassed dose draws no throw on the way.
        Assert.Equal(DaggerfallPoisonAdmission.Immune, fixture.Poisoning.UseDrug(78));
        Assert.Equal(0, fixture.Poisoning.Draws);
        Assert.Throws<ArgumentOutOfRangeException>(() => fixture.Poisoning.UseDrug(77));

        fixture.Actors.Player.Progression.AdvanceTo(500, 5);
        Assert.Equal(DaggerfallPoisonAdmission.Admitted, fixture.Poisoning.UseDrug(78));
        Assert.True(fixture.Poisoning.Runtime.IsAfflicted(fixture.Player));
        Assert.True(fixture.Poisoning.CurePlayer());
        Assert.False(fixture.Poisoning.Runtime.IsAfflicted(fixture.Player));
    }

    [Fact]
    public void A_coating_reaches_a_site_actor_at_its_own_level_and_is_spent_whatever_the_throw()
    {
        using Fixture first = new(siteActorLevel: 1);
        ulong weapon = first.CoatedWeapon(128);
        first.Poisoning.DeliverWeaponPoison(SiteActor, weapon);
        // A first-level target is refused after the throw the donor still draws, and the dose is spent anyway.
        Assert.False(first.Poisoning.Runtime.IsAfflicted(first.Site));
        Assert.Equal(1, first.Poisoning.Draws);
        Assert.Null(first.Items.RequireUnique(weapon).PoisonVariant);

        using Fixture levelled = new(siteActorLevel: 5);
        weapon = levelled.CoatedWeapon(128);
        levelled.Poisoning.DeliverWeaponPoison(SiteActor, weapon);
        Assert.True(levelled.Poisoning.Runtime.IsAfflicted(levelled.Site));
        Assert.Null(levelled.Items.RequireUnique(weapon).PoisonVariant);

        // A coating the strike did not admit, or a weapon with none, delivers nothing.
        weapon = levelled.CoatedWeapon(128);
        levelled.Poisoning.DeliverWeaponPoison(SiteActor, new DaggerfallWeaponPoisonSource(weapon, 129));
        Assert.Equal(128, levelled.Items.RequireUnique(weapon).PoisonVariant);
    }

    [Fact]
    public void A_site_actor_without_an_admitted_level_cannot_be_poisoned()
    {
        using Fixture fixture = new(siteActorLevel: null);
        ulong weapon = fixture.CoatedWeapon(128);
        Assert.Throws<InvalidOperationException>(() => fixture.Poisoning.DeliverWeaponPoison(SiteActor, weapon));
    }

    [Fact]
    public void The_draw_continues_from_its_saved_ordinal()
    {
        using Fixture fixture = new(draws: 41);
        fixture.Actors.Player.Progression.AdvanceTo(500, 5);
        DaggerfallPoisonExposure thrown = fixture.Poisoning.PlayerExposure(bypassResistance: false);
        Assert.Equal(DaggerfallPoisonAdmission.Admitted, fixture.Poisoning.Inflict(thrown, 136));
        Assert.True(fixture.Poisoning.Draws > 41);
    }
}
