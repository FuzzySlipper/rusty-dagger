using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Joining a guild, built from the admission's inputs without a session.</summary>
public sealed class DaggerfallGuildAdmissionTests
{
    private static readonly DaggerfallConcreteGuildDefinition Fighters =
        DaggerfallConcreteGuildCatalog.ForFaction(DaggerfallConcreteGuildCatalog.FightersFactionId);

    [Fact]
    public void A_player_below_the_guilds_standing_hears_its_reputation_refusal()
    {
        using GuildStage stage = new();
        stage.Society.Social.ChangeFactionReputation(Fighters.FactionId, -50, DaggerfallFactionReputationChange.Direct);
        DaggerfallNpc npc = stage.Provider(DaggerfallConcreteGuildCatalog.FightersTrainerFactionId, "training");

        DaggerfallGuildProviderResult refused = stage.Admission.Join(Fighters, npc, 0, null);

        Assert.Equal((false, DaggerfallGuildProviderDenial.NotEligible, 679), (refused.Accepted, refused.Denial, refused.TextResource));
        Assert.Equal("The Fighters Guild", refused.GuildName);
    }

    [Fact]
    public void Declining_or_an_answer_without_an_offer_admits_no_one()
    {
        using GuildStage stage = new();
        DaggerfallNpc npc = stage.Provider(DaggerfallConcreteGuildCatalog.FightersTrainerFactionId, "training");

        Assert.Equal("You decline to join.", stage.Admission.Join(Fighters, npc, 0, "no").Message);
        DaggerfallGuildProviderResult answer = stage.Admission.Join(Fighters, npc, 0, "yes");
        Assert.False(answer.Accepted);
        Assert.False(stage.Membership.Read(Fighters.FactionId, 0).IsMember);
    }

    [Fact]
    public void An_offer_is_good_for_its_dialogue_page_and_accepting_it_admits_the_player()
    {
        using GuildStage stage = new();
        // The guild's skills, raised well past its first rank's.
        foreach (string skill in Fighters.GuildSkills)
            DaggerfallStatModifiers.AdjustPermanent(stage.Society.Actors.Player.Stats, new DaggerfallStatId(skill), 40);
        DaggerfallNpc npc = stage.Provider(DaggerfallConcreteGuildCatalog.FightersTrainerFactionId, "training");

        DaggerfallGuildProviderResult offer = stage.Admission.Join(Fighters, npc, 0, null);
        Assert.Equal((true, 681), (offer.Accepted, offer.TextResource));
        Assert.Equal(["yes", "no"], offer.Options!.Select(option => option.Key));

        stage.Revision = "page:2";
        Assert.Equal(DaggerfallGuildProviderDenial.OfferExpired, stage.Admission.Join(Fighters, npc, 0, "yes").Denial);

        Assert.True(stage.Admission.Join(Fighters, npc, 0, null).Accepted);
        DaggerfallGuildProviderResult welcome = stage.Admission.Join(Fighters, npc, 0, "yes");
        Assert.Equal((true, 684), (welcome.Accepted, welcome.TextResource));
        Assert.True(stage.Membership.Read(Fighters.FactionId, 0).IsMember);
    }

    [Fact]
    public void A_member_is_told_so()
    {
        using GuildStage stage = new();
        _ = stage.Society.Social.JoinGuild(Fighters.FactionId, currentDay: 0);
        DaggerfallNpc npc = stage.Provider(DaggerfallConcreteGuildCatalog.FightersTrainerFactionId, "training");

        Assert.Equal("You already belong to this guild.", stage.Admission.Join(Fighters, npc, 0, null).Message);
    }
}
