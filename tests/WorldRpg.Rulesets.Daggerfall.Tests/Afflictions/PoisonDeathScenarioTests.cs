using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Combat;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Facts;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// #10748's first scenario, harm to its consequences, as far as it runs without the session: a coated blade's poison
/// reaches a site actor (Afflictions), its minutes act through the effect lifecycle (Magic) and the vitality owner
/// (Condition), that owner reports the death, and Actors' damage facts turn it into <c>ActorDiedFact</c>. What reacts to
/// the death (effects ended, the corpse, the rewards, the quests' foes, reputation) is still the session's fact
/// reaction, until Quests and Society are owners.
/// </summary>
public sealed class PoisonDeathScenarioTests
{
    [Fact]
    public void A_coated_blade_poisons_a_site_actor_to_death_and_the_death_becomes_a_fact()
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

        // A poison's minute is effect harm, as the session hands the vitality owner's report to the damage facts.
        FactBuffer<IProductFact> facts = new();
        DaggerfallDamageFacts.Append(facts, death, DaggerfallDamageCause.Effect, 0, 7, 3);
        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        ActorDiedFact died = Assert.Single(delivered.OfType<ActorDiedFact>());
        Assert.Equal(PoisoningFixture.SiteActor, died.ActorId);
        Assert.Equal(DaggerfallDamageCause.Effect, died.Cause);
        Assert.Equal((7UL, 3UL), (died.OriginatingGeneration, died.OriginatingSequence));
        Assert.IsType<DamageAppliedFact>(delivered[0]);
        Assert.True(Assert.IsType<ActorDamagedFact>(delivered[1]).TargetDefeated);
    }
}
