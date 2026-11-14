using WorldRpg.Kit.Progression;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Crime;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Quest work built without the session: <see cref="GuildMakerFixture"/>'s people, standing and paid services, quests,
/// crime and a site directory of its own, with the hall's guild, the interior, the profile's kind and the castle zone as
/// plain inputs.
/// </summary>
public sealed class DaggerfallQuestWorkTests
{
    private static readonly DaggerfallNpcSite FixtureSite = new(0, "Daggerfall", "guild");

    private sealed class Stage : IDisposable
    {
        internal GuildMakerFixture Makers { get; } = new();
        internal DaggerfallQuestInstances Quests { get; }
        internal DaggerfallCrimeState Crime { get; }
        internal DaggerfallSiteContext Sites { get; }
        internal DaggerfallWorldProfileKind Kind { get; set; } = DaggerfallWorldProfileKind.Exterior;

        internal Stage(DaggerfallCrimeSave? crime = null)
        {
            Quests = new(Makers.Definitions, SummonRandom.Create());
            Crime = new(crime, () => false);
            Sites = new(Makers.Definitions.Locations);
            Sites.Enter(Makers.Definitions.Locations.Records.First(site => site.Exterior is not null).Id);
            Makers.Npcs.Identities = new WorldRpg.Kit.World.DurableIdentityAllocator(WorldRpg.Kit.World.DurableIdentityKind.Actor, 100);
        }

        internal DaggerfallQuestWork Work(bool insideCastle = false) => new(Quests, Makers.Npcs, Makers.Services, Makers.Social, Crime,
            (_, _) => throw new InvalidOperationException("No guild hall is entered in this stage."), Makers.Definitions, new ProgressionState(),
            new DaggerfallWorldTime(DaggerfallCalendar.FromAbsoluteSeconds(100L * DaggerfallCalendar.SecondsPerDay), 0d, 12d), Sites, DaggerfallTuning.Defaults.QuestOffers,
            () => null, () => Kind, () => insideCastle);

        public void Dispose() => Makers.Dispose();
    }

    [Fact]
    public void A_witch_with_quest_work_has_work_and_a_stranger_has_none()
    {
        using Stage stage = new();
        var faction = stage.Makers.Definitions.Factions.Factions[419];
        var facts = DaggerfallNpcServiceFacts.Resolve(stage.Makers.Definitions, faction, -1, 0, "witch");
        long witch = stage.Makers.Npcs.RegisterStable(DaggerfallNpcKind.Static, "coven-work", FixtureSite,
            new("breton", "Female", 184, 8, 0, faction.Id), facts.Role, facts.Services);
        DaggerfallQuestWork work = stage.Work();

        Assert.True(work.HasWork(witch));
        Assert.False(work.HasWork(987654321));
        Assert.Equal("That provider has no work available.", work.Offer(987654321));
    }

    [Fact]
    public void A_castle_talker_offers_no_work_outside_a_castle_zone()
    {
        using Stage stage = new();
        var faction = stage.Makers.Definitions.Factions.Factions.Values.First(value => value.Type != 8
            && !DaggerfallConcreteGuildCatalog.All.Any(guild => guild.FactionId == value.Id));
        long talker = stage.Makers.Npcs.RegisterStable(DaggerfallNpcKind.Static, "castle-talker", FixtureSite,
            new("breton", "Male", 184, 8, 0, faction.Id), "noble", ["talk"]);

        Assert.False(stage.Work().HasWork(talker));
        Assert.Equal("That provider has no work available.", stage.Work().Offer(talker));
    }

    [Fact]
    public void A_due_thieves_invitation_starts_its_quest_once_and_never_while_the_player_is_inside()
    {
        using Stage stage = new(new DaggerfallCrimeSave([], [], [], ThievingRequirementTally: 10, MurderRequirementTally: 0,
            ThievesInvitationDueMinute: 1, MurderInvitationDueMinute: 0));
        stage.Kind = DaggerfallWorldProfileKind.Interior;
        Assert.Empty(stage.Work().StartDueInvitations());
        Assert.Empty(stage.Quests.All);

        stage.Kind = DaggerfallWorldProfileKind.Exterior;
        Assert.Empty(stage.Work().StartDueInvitations());
        Assert.Equal("crime-invitation:O0A0AL00", Assert.Single(stage.Quests.All).InstanceId);
        Assert.Empty(stage.Work().StartDueInvitations());
        Assert.Single(stage.Quests.All);
    }
}
