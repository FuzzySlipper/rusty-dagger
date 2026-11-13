using Rusty.Engine;
using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Who a poison reaches, built without the session: the player's exposure, a weapon's coating on a site actor,
/// drugs, and the admitted draw's saved ordinal.
/// </summary>
public sealed class DaggerfallPoisoningTests
{
    [Fact]
    public void A_drug_reaches_the_player_past_the_first_level_and_is_cured_by_the_player_cure()
    {
        using PoisoningFixture fixture = new();
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
        using PoisoningFixture first = new(siteActorLevel: 1);
        ulong weapon = first.CoatedWeapon(128);
        first.Poisoning.DeliverWeaponPoison(PoisoningFixture.SiteActor, weapon);
        // A first-level target is refused after the throw the donor still draws, and the dose is spent anyway.
        Assert.False(first.Poisoning.Runtime.IsAfflicted(first.Site));
        Assert.Equal(1, first.Poisoning.Draws);
        Assert.Null(first.Items.RequireUnique(weapon).PoisonVariant);

        using PoisoningFixture levelled = new(siteActorLevel: 5);
        weapon = levelled.CoatedWeapon(128);
        levelled.Poisoning.DeliverWeaponPoison(PoisoningFixture.SiteActor, weapon);
        Assert.True(levelled.Poisoning.Runtime.IsAfflicted(levelled.Site));
        Assert.Null(levelled.Items.RequireUnique(weapon).PoisonVariant);

        // A coating the strike did not admit, or a weapon with none, delivers nothing.
        weapon = levelled.CoatedWeapon(128);
        levelled.Poisoning.DeliverWeaponPoison(PoisoningFixture.SiteActor, new DaggerfallWeaponPoisonSource(weapon, 129));
        Assert.Equal(128, levelled.Items.RequireUnique(weapon).PoisonVariant);
    }

    [Fact]
    public void A_site_actor_without_an_admitted_level_cannot_be_poisoned()
    {
        using PoisoningFixture fixture = new(siteActorLevel: null);
        ulong weapon = fixture.CoatedWeapon(128);
        Assert.Throws<InvalidOperationException>(() => fixture.Poisoning.DeliverWeaponPoison(PoisoningFixture.SiteActor, weapon));
    }

    [Fact]
    public void The_draw_continues_from_its_saved_ordinal()
    {
        using PoisoningFixture fixture = new(draws: 41);
        fixture.Actors.Player.Progression.AdvanceTo(500, 5);
        DaggerfallPoisonExposure thrown = fixture.Poisoning.PlayerExposure(bypassResistance: false);
        Assert.Equal(DaggerfallPoisonAdmission.Admitted, fixture.Poisoning.Inflict(thrown, 136));
        Assert.True(fixture.Poisoning.Draws > 41);
    }
}
