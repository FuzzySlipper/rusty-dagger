using WorldRpg.Rulesets.Daggerfall.Guilds;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>The guild providers' own rules, built from their inputs without a session.</summary>
public sealed class DaggerfallGuildProvidersTests
{
    private const int Fighters = DaggerfallConcreteGuildCatalog.FightersFactionId;
    private const int Trainer = DaggerfallConcreteGuildCatalog.FightersTrainerFactionId;

    [Fact]
    public void A_hall_provider_resolves_to_its_guild_and_refuses_with_its_most_specific_reason()
    {
        using GuildStage stage = new();
        DaggerfallNpc trainer = stage.Provider(Trainer, "training");

        Assert.True(stage.Providers.TryResolve(trainer, GuildStage.Hall(Fighters), null, out var guild, out var provider, out _));
        Assert.Equal(Fighters, guild!.FactionId);
        Assert.Equal(new DaggerfallServiceProvider(trainer.DurableId, trainer.Site, "training"), provider);

        stage.Availability = DaggerfallServiceDenial.SiteChanged;
        Assert.False(stage.Providers.TryResolve(trainer, GuildStage.Hall(Fighters), null, out _, out _, out var denial));
        Assert.Equal(DaggerfallGuildProviderDenial.SiteChanged, denial);

        DaggerfallNpc stranger = stage.Provider(999, "training");
        Assert.False(stage.Providers.TryResolve(stranger, GuildStage.Hall(Fighters), null, out _, out _, out denial));
        Assert.Equal(DaggerfallGuildProviderDenial.UnknownProvider, denial);
    }

    [Fact]
    public void A_topic_outside_a_guild_hall_or_from_a_stranger_is_refused()
    {
        using GuildStage stage = new();
        DaggerfallNpc trainer = stage.Provider(Trainer, "training");

        Assert.Equal(DaggerfallGuildProviderDenial.InvalidBuilding,
            stage.Providers.Resolve(trainer, null, DaggerfallGuildProviderTopic.RankReview, null).Denial);
        Assert.Equal(DaggerfallGuildProviderDenial.InvalidBuilding,
            stage.Providers.Resolve(trainer, GuildStage.Hall(Fighters, buildingType: 2), DaggerfallGuildProviderTopic.RankReview, null).Denial);
        DaggerfallGuildProviderResult stranger = stage.Providers.Resolve(stage.Provider(999, "training"), GuildStage.Hall(Fighters),
            DaggerfallGuildProviderTopic.RankReview, null);
        Assert.Equal("That person is not an admitted provider for this guild interior.", stranger.Message);
    }

    [Fact]
    public void A_rank_review_answers_a_stranger_and_a_member()
    {
        using GuildStage stage = new();
        DaggerfallNpc trainer = stage.Provider(Trainer, "training");

        DaggerfallGuildProviderResult stranger = stage.Providers.Resolve(trainer, GuildStage.Hall(Fighters), DaggerfallGuildProviderTopic.RankReview, null);
        Assert.Equal((false, DaggerfallGuildProviderDenial.NotMember, "You are not a member of this guild."), (stranger.Accepted, stranger.Denial, stranger.Message));

        _ = stage.Society.Social.JoinGuild(Fighters, currentDay: 0);
        DaggerfallGuildProviderResult member = stage.Providers.Resolve(trainer, GuildStage.Hall(Fighters), DaggerfallGuildProviderTopic.RankReview, null);
        Assert.True(member.Accepted);
        Assert.NotNull(member.Membership);
    }

    [Fact]
    public void No_guild_grants_free_passage_to_a_stranger_and_a_stranger_has_no_welcome()
    {
        using GuildStage stage = new();
        Assert.False(stage.Providers.FreeShipPassage());
        DaggerfallNpc trainer = stage.Provider(Trainer, "training");
        Assert.Null(stage.Providers.Benefits(trainer, null));
        DaggerfallGuildProviderResult welcome = stage.Providers.Benefits(trainer, GuildStage.Hall(Fighters))!;
        Assert.Equal((true, ""), (welcome.Accepted, welcome.Message));
        Assert.Null(welcome.Options);
        Assert.Empty(stage.Teleports);
        Assert.Empty(stage.Houses);
    }
}
