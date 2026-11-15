using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Facts;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Facts;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The facts a spell's harm becomes, built over a plain actor store with no session.</summary>
public sealed class DaggerfallDamageFactsTests
{
    private static readonly TrackId Fatigue = TrackId.Parse(DaggerfallMechanicsIds.Fatigue.Value);
    private static readonly TrackId Magicka = TrackId.Parse(DaggerfallMechanicsIds.Magicka.Value);

    [Fact]
    public void A_track_loss_is_fatigues_own_fact_for_fatigue_and_spell_points_for_any_other_track()
    {
        using ActorsState actors = new();
        (ActorState caster, ActorState target) = Pair(actors);
        FactBuffer<IProductFact> facts = new();

        DaggerfallDamageFacts.AppendTrackLoss(facts, new(caster.Actor, target.Actor, Fatigue, 6, 5d), 3, 4);
        DaggerfallDamageFacts.AppendTrackLoss(facts, new(caster.Actor, target.Actor, Magicka, 2, 2d), 3, 4);

        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.Equal([new FatigueAppliedFact(300, 301, 6, 5d, 3, 4), new SpellPointsAppliedFact(300, 301, 2, 2d, 3, 4)], delivered);
    }

    [Fact]
    public void A_transfer_reports_its_track_loss_before_the_transfer_itself()
    {
        using ActorsState actors = new();
        (ActorState caster, ActorState target) = Pair(actors);
        FactBuffer<IProductFact> facts = new();
        DaggerfallSpellTrackResult loss = new(caster.Actor, target.Actor, Fatigue, 6, 5d);

        DaggerfallDamageFacts.AppendTransfer(facts, new(caster.Actor, target.Actor, Fatigue, 6, null, loss, 5d), 3, 4);

        List<IProductFact> delivered = [];
        facts.Deliver(delivered.Add);
        Assert.Equal(2, delivered.Count);
        Assert.Equal(new FatigueAppliedFact(300, 301, 6, 5d, 3, 4), delivered[0]);
        VitalTransferredFact transfer = Assert.IsType<VitalTransferredFact>(delivered[1]);
        Assert.Equal((300L, 301L, Fatigue.Value, 6, 5d, 5d, false), (transfer.CasterId, transfer.TargetId, transfer.Track,
            transfer.AdmittedAmount, transfer.ActualLoss, transfer.ActualRecovery, transfer.TargetDefeated));
    }

    private static (ActorState Caster, ActorState Target) Pair(ActorsState actors)
    {
        ActorState Create(long id) => actors.CreateActor(id, new EntityTypeId("rat"), TestSessions.HealthyMechanics(),
            new ActorPose(new WorldPoint(0f, 0f, 0f), 0f), "health");
        return (Create(300), Create(301));
    }
}
