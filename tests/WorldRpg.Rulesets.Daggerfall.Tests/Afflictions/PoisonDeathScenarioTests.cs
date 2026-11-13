using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// #10748's first scenario, harm to its consequences, as far as it runs without the session: a coated blade's poison
/// reaches a site actor (Afflictions), its minutes act through the effect lifecycle (Magic) and the vitality owner
/// (Condition), and that owner reports the death. Turning the death into <c>ActorDiedFact</c> and what follows it
/// (the corpse, the rewards, the quests' foes, reputation) is still the session's, until Actors, Society and Quests
/// are owners.
/// </summary>
public sealed class PoisonDeathScenarioTests
{
    [Fact]
    public void A_coated_blade_poisons_a_site_actor_to_death_and_the_vitality_owner_reports_it()
    {
        using PoisoningFixture fixture = new(siteActorLevel: 5);
        List<DamageResult> applied = [];
        fixture.Vitality.PoisonDamageApplied += applied.Add;
        Track health = fixture.Site.Get<StatsComponent>().GetTrack(TrackId.Parse("health"));
        health.SetCurrent(3);

        // Nux Vomica: its onset is up to four minutes, then each minute takes health.
        fixture.Poisoning.DeliverWeaponPoison(PoisoningFixture.SiteActor, fixture.CoatedWeapon(128));
        Assert.True(fixture.Poisoning.Runtime.IsAfflicted(fixture.Site));
        for (int minute = 0; minute < 20 && !(health.Current <= 0); minute++) fixture.Effects.AdvanceOrdinaryRound();

        DamageResult death = Assert.Single(applied, result => result.Defeated);
        Assert.Equal(fixture.Site.Entity, death.Target.Entity);
        Assert.True(fixture.Actors.TryGet(PoisoningFixture.SiteActor, out ActorState victim) && victim.IsDefeated);
    }
}
