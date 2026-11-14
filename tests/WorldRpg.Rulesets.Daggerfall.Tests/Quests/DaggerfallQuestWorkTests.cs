using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Quest work built without the session: the fixture's quests, people, services, standing and crime, with the interior
/// building, the profile's kind and the castle zone as plain inputs.
/// </summary>
public sealed class DaggerfallQuestWorkTests
{
    private static DaggerfallQuestWork Work(DaggerfallSession session, bool insideCastle = false)
    {
        DaggerfallState state = session.State;
        return new(state.Quests, state.Npcs, state.Services, state.Social, state.Crime, session.GuildProviders, TestPayload.Definitions,
            state.Progression, new DaggerfallWorldTime(DaggerfallCalendar.Start, 0d, 12d), session.Site,
            DaggerfallTuning.Defaults.QuestOffers, () => null, () => DaggerfallWorldProfileKind.Exterior, () => insideCastle);
    }

    [Fact]
    public void A_witch_with_quest_work_offers_it_and_accepting_starts_the_quest()
    {
        // A quest's places are allocated from the block markers, so the fixture admits them.
        using SanguineRoseSessionTests.Fixture fixture = new(random: SummonRandom.Create(), prepareComposition: composition => composition with {
            Blocks = DaggerfallBlocksContent.Read(TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.blocks.json"))) });
        DaggerfallState state = fixture.Session.State;
        var faction = TestPayload.Definitions.Factions.Factions[419];
        var facts = DaggerfallNpcServiceFacts.Resolve(TestPayload.Definitions, faction, -1, 0, "witch");
        DaggerfallSiteRecord site = fixture.Session.Site.ActiveSite!;
        long provider = state.Npcs.RegisterStable(DaggerfallNpcKind.Static, "coven-work", new(site.Region, site.Name, string.Empty),
            new("breton", "Female", 184, 8, 0, faction.Id), facts.Role, facts.Services);
        DaggerfallQuestWork work = Work(fixture.Session);

        Assert.True(work.HasWork(provider));
        string text = work.Offer(provider);
        Assert.True(state.Quests.PendingOffer is not null, text);
        DaggerfallQuestOfferSave offer = Assert.IsType<DaggerfallQuestOfferSave>(state.Quests.PendingOffer);
        Assert.Equal(state.Quests.ReadOffer()!.Text, text);
        Assert.StartsWith("Q0", offer.Quest.DefinitionName);

        work.Answer(offer.Quest.InstanceId, true);
        Assert.Equal(offer.Quest.InstanceId, Assert.Single(state.Quests.All).InstanceId);
        Assert.False(work.HasWork(provider));
    }

    [Fact]
    public void A_castle_talker_offers_work_only_inside_a_castle_zone_and_nobody_unknown_does()
    {
        using ConditionSessionFixture fixture = new(SummonRandom.Create());
        DaggerfallState state = fixture.Session.State;
        var faction = fixture.Definitions.Factions.Factions.Values.First(value => value.Type != 8
            && !DaggerfallConcreteGuildCatalog.All.Any(guild => guild.FactionId == value.Id));
        DaggerfallSiteRecord site = fixture.Session.Site.ActiveSite!;
        long talker = state.Npcs.RegisterStable(DaggerfallNpcKind.Static, "castle-talker", new(site.Region, site.Name, string.Empty),
            new("breton", "Male", 184, 8, 0, faction.Id), "noble", ["talk"]);

        Assert.Equal("That provider has no work available.", Work(fixture.Session).Offer(talker));
        Assert.Equal("That provider has no work available.", Work(fixture.Session, insideCastle: true).Offer(987654321));
        Assert.Empty(Work(fixture.Session).StartDueInvitations());
        Assert.Empty(state.Quests.All);
    }
}
