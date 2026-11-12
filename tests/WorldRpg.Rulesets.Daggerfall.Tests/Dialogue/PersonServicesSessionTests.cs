using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The person screen's chooser lists what the ruleset publishes for the person spoken to: source-backed
/// providers publish their services in order with their names, and a person who offers only talk opens on it.
/// </summary>
public sealed class PersonServicesSessionTests
{
    [Fact]
    public void A_shopkeeper_offers_talk_and_the_trade_it_deals_in()
    {
        using SourceBackedServiceSessionFixture fixture = SourceBackedServiceSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.GenericRepairProvider.Profile);
        session.AdvanceElapsedTime(6 * 60 * 60);
        DaggerfallDialogueView dialogue = Open(session, fixture.GenericRepairProvider.Placement.Id);

        DaggerfallMerchantView merchant = Assert.IsType<DaggerfallMerchantView>(dialogue.Merchant);
        Assert.True(merchant is { CanBuy: true, CanSell: true, CanRepair: true, CanIdentify: false });
        Assert.Equal([("talk", "Talk"), ("trade", "Buy, sell and repair")],
            dialogue.Services.Select(service => (service.Id, service.Label)));
        Assert.Equal(DaggerfallPersonServices.Chooser, dialogue.OpensOn);
    }

    [Fact]
    public void A_guild_trainer_offers_talk_and_training()
    {
        using SourceBackedServiceSessionFixture fixture = SourceBackedServiceSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.TrainingProvider.Profile);
        session.AdvanceElapsedTime(6 * 60 * 60, idleFatigue: false);
        DaggerfallDialogueView dialogue = Open(session, fixture.TrainingProvider.Placement.Id);

        Assert.NotNull(dialogue.Training);
        Assert.Equal([("talk", "Talk"), ("training", "Training")], dialogue.Services.Select(service => (service.Id, service.Label)));
        Assert.Equal(DaggerfallPersonServices.Chooser, dialogue.OpensOn);
    }

    [Fact]
    public void A_temple_priest_takes_donations_in_talk_and_opens_straight_on_it()
    {
        using SourceBackedServiceSessionFixture fixture = SourceBackedServiceSessionFixture.Create();
        SourceBackedServiceSessionFixture.SourceProvider source = fixture.GuildProvider("donate");
        using DaggerfallSession session = fixture.Start(source.Profile);
        DaggerfallDialogueView dialogue = Open(session, source.Placement.Id);

        Assert.Contains(dialogue.Topics, topic => topic.Id == "donate");
        Assert.Equal(["talk"], dialogue.Services.Select(service => service.Id));
        Assert.Equal(DaggerfallPersonServices.Talk, dialogue.OpensOn);
    }

    [Fact]
    public void A_townsperson_offers_only_talk_and_opens_straight_on_it()
    {
        using SourceBackedServiceSessionFixture fixture = SourceBackedServiceSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.CityProfile);
        session.AdvanceElapsedTime(6 * 60 * 60);
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        DaggerfallNpc npc = session.State.Npcs.All.First(value => value.Kind == DaggerfallNpcKind.Civilian
            && value.StableKey.StartsWith("population/", StringComparison.Ordinal) && value.Presence == DaggerfallNpcPresence.Active);
        DaggerfallDialogueView dialogue = Open(session, npc);

        Assert.Equal([("talk", "Talk")], dialogue.Services.Select(service => (service.Id, service.Label)));
        Assert.Equal(DaggerfallPersonServices.Talk, dialogue.OpensOn);
    }

    private static DaggerfallDialogueView Open(DaggerfallSession session, string stableKey) => Open(session,
        Assert.Single(session.State.Npcs.All, value => value.StableKey == stableKey
            && value.Kind == DaggerfallNpcKind.Static && value.Presence == DaggerfallNpcPresence.Active));

    private static DaggerfallDialogueView Open(DaggerfallSession session, DaggerfallNpc npc)
    {
        DaggerfallActivationTarget target = Assert.Single(session.Dialogue.NpcTargets(),
            value => value.Identity.Value == checked((ulong)npc.DurableId));
        DaggerfallActivationOutcome outcome = session.Dialogue.ActivateNpc(new(DaggerfallActivationMode.Talk, target));
        Assert.True(outcome.Applied, outcome.Message);
        return Assert.IsType<DaggerfallDialogueView>(session.ActivationView.Dialogue);
    }
}
