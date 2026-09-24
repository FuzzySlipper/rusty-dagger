using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallConcreteGuildCatalogTests
{
    [Fact]
    public void Catalog_retains_standalone_temples_and_all_ten_regional_orders()
    {
        Assert.Equal(22, DaggerfallConcreteGuildCatalog.All.Count);
        Assert.Equal(4, DaggerfallConcreteGuildCatalog.All.Count(definition => definition.MembershipKind == DaggerfallGuildMembershipKind.Standalone));
        Assert.Equal(8, DaggerfallConcreteGuildCatalog.All.Count(definition => definition.MembershipKind == DaggerfallGuildMembershipKind.TempleDeity));
        Assert.Equal(10, DaggerfallConcreteGuildCatalog.All.Count(definition => definition.MembershipKind == DaggerfallGuildMembershipKind.KnightlyOrder));

        DaggerfallConcreteGuildDefinition fighters = DaggerfallConcreteGuildCatalog.ForFaction(41);
        Assert.Equal(11, fighters.GuildGroup);
        Assert.Equal(849, fighters.TrainingProviderFactionId);
        Assert.Equal(
            ["archery", "axe", "blunt-weapon", "critical-strike", "giantish", "jumping", "long-blade", "orcish", "running", "short-blade", "swimming"],
            fighters.TrainingSkills);

        DaggerfallConcreteGuildDefinition raven = DaggerfallConcreteGuildCatalog.ForFaction(414);
        Assert.Equal(9, raven.GuildGroup);
        Assert.Equal(200, raven.ParentFactionId);
        Assert.Equal(5, raven.Region);
        Assert.Equal(DaggerfallConcreteGuildKind.KnightlyOrder, raven.Kind);
        Assert.False(raven.Services.Single(service => service.Service == DaggerfallConcreteGuildService.Quests).RequiresMembership);

        DaggerfallConcreteGuildDefinition arkay = DaggerfallConcreteGuildCatalog.ForFaction(82);
        Assert.Equal(17, arkay.GuildGroup);
        Assert.Equal(21, arkay.ParentFactionId);
        Assert.Equal(241, arkay.TrainingProviderFactionId);
        Assert.Equal(82, arkay.TrainingProviderMembershipFactionId);
    }

    [Fact]
    public void Training_catalog_is_read_from_the_existing_provider_policy()
    {
        foreach (DaggerfallConcreteGuildDefinition definition in DaggerfallConcreteGuildCatalog.All)
        {
            if (definition.TrainingProviderFactionId is not int providerId) continue;

            Assert.True(DaggerfallSkillTrainingPolicy.TryGetProvider(providerId, out DaggerfallSkillTrainingProviderPolicy provider));
            Assert.Equal(definition.FactionId, provider.MembershipFactionId);
            Assert.Equal(provider.MembershipFactionId, definition.TrainingProviderMembershipFactionId);
            Assert.Equal(provider.Skills, definition.TrainingSkills);
        }
    }

    [Fact]
    public void Crime_invitation_conditions_consume_qualification_facts_without_a_tally()
    {
        DaggerfallConcreteGuildDefinition thieves = DaggerfallConcreteGuildCatalog.ForFaction(42);
        DaggerfallConcreteGuildDefinition brotherhood = DaggerfallConcreteGuildCatalog.ForFaction(108);
        DaggerfallGuildCrimeInvitationEvidence none = new(false, false);
        DaggerfallGuildCrimeInvitationEvidence both = new(true, true);

        Assert.Equal(DaggerfallGuildInvitationRequirement.ThievingCrime, thieves.InvitationRequirement);
        Assert.Equal(DaggerfallGuildInvitationRequirement.MurderCrime, brotherhood.InvitationRequirement);
        Assert.False(DaggerfallConcreteGuildPolicy.IsInvitationEligible(thieves, none));
        Assert.True(DaggerfallConcreteGuildPolicy.IsInvitationEligible(thieves, both));
        Assert.False(DaggerfallConcreteGuildPolicy.IsInvitationEligible(brotherhood, none));
        Assert.True(DaggerfallConcreteGuildPolicy.IsInvitationEligible(brotherhood, both));

        Dictionary<string, int> thiefSkills = new(StringComparer.Ordinal)
        {
            ["backstabbing"] = 22,
            ["climbing"] = 4,
        };
        DaggerfallGuildAdmissionDecision waiting = DaggerfallConcreteGuildPolicy.AssessAdmission(
            thieves, new(0, thiefSkills, none));
        DaggerfallGuildAdmissionDecision invited = DaggerfallConcreteGuildPolicy.AssessAdmission(
            thieves, new(0, thiefSkills, new(true, false)));
        Assert.True(waiting.RankEligible);
        Assert.False(waiting.InvitationEligible);
        Assert.Equal(DaggerfallGuildAdmissionDenial.MissingCrimeEvidence, waiting.Denial);
        Assert.True(invited.Eligible);
    }

    [Fact]
    public void Mages_recharge_requires_no_regeneration_and_hall_rank_six_is_not_rest()
    {
        DaggerfallConcreteGuildDefinition mages = DaggerfallConcreteGuildCatalog.ForFaction(40);
        DaggerfallGuildServiceDecision noRegen = DaggerfallConcreteGuildPolicy.EvaluateService(
            mages, DaggerfallConcreteGuildService.FreeMagickaRecharge, new(true, 0, NoRegenSpellPoints: false));
        DaggerfallGuildServiceDecision recharge = DaggerfallConcreteGuildPolicy.EvaluateService(
            mages, DaggerfallConcreteGuildService.FreeMagickaRecharge, new(true, 0, NoRegenSpellPoints: true));

        Assert.Equal(DaggerfallGuildServiceDenial.RequiresNoRegenSpellPoints, noRegen.Denial);
        Assert.True(recharge.Eligible);
        Assert.False(mages.TryGetService(DaggerfallConcreteGuildService.Rest, out _));
        Assert.Equal(6, mages.Services.Single(service => service.Service == DaggerfallConcreteGuildService.HallAccess).MinimumRank);
    }

    [Fact]
    public void Temple_absent_rank_data_is_omitted_and_blessing_is_explicitly_unavailable()
    {
        DaggerfallConcreteGuildDefinition julianos = DaggerfallConcreteGuildCatalog.ForFaction(94);
        DaggerfallConcreteGuildDefinition arkay = DaggerfallConcreteGuildCatalog.ForFaction(82);

        Assert.False(julianos.TryGetService(DaggerfallConcreteGuildService.BuyPotions, out _));
        Assert.True(julianos.TryGetService(DaggerfallConcreteGuildService.BuyMagicItems, out _));
        DaggerfallGuildServiceDecision blessing = DaggerfallConcreteGuildPolicy.EvaluateService(
            arkay, DaggerfallConcreteGuildService.Blessing, new(true, 9));
        Assert.Equal(DaggerfallGuildServiceDecisionKind.Unavailable, blessing.Kind);
        Assert.Equal(DaggerfallGuildServiceDenial.Unavailable, blessing.Denial);
    }

    [Fact]
    public void Knightly_rooms_use_home_region_or_rank_four_and_claims_block_repeat_rewards()
    {
        DaggerfallConcreteGuildDefinition raven = DaggerfallConcreteGuildCatalog.ForFaction(414);
        DaggerfallGuildServiceDecision homeRegion = DaggerfallConcreteGuildPolicy.EvaluateService(
            raven, DaggerfallConcreteGuildService.FreeTavernRooms, new(true, 0, CurrentRegion: 5));
        DaggerfallGuildServiceDecision away = DaggerfallConcreteGuildPolicy.EvaluateService(
            raven, DaggerfallConcreteGuildService.FreeTavernRooms, new(true, 0, CurrentRegion: 6));
        DaggerfallGuildServiceDecision highRank = DaggerfallConcreteGuildPolicy.EvaluateService(
            raven, DaggerfallConcreteGuildService.FreeTavernRooms, new(true, 4, CurrentRegion: 6));
        DaggerfallGuildServiceDecision armor = DaggerfallConcreteGuildPolicy.EvaluateService(
            raven, DaggerfallConcreteGuildService.ReceiveArmor, new(true, 3, OrderClaims: new(new HashSet<int>(), false)));
        DaggerfallGuildServiceDecision claimedArmor = DaggerfallConcreteGuildPolicy.EvaluateService(
            raven, DaggerfallConcreteGuildService.ReceiveArmor, new(true, 3, OrderClaims: new(new HashSet<int> { 3 }, false)));
        DaggerfallGuildServiceDecision house = DaggerfallConcreteGuildPolicy.EvaluateService(
            raven, DaggerfallConcreteGuildService.ReceiveHouse, new(true, 9, OrderClaims: new(new HashSet<int>(), false)));
        DaggerfallGuildServiceDecision claimedHouse = DaggerfallConcreteGuildPolicy.EvaluateService(
            raven, DaggerfallConcreteGuildService.ReceiveHouse, new(true, 9, OrderClaims: new(new HashSet<int>(), true)));

        Assert.True(homeRegion.Eligible);
        Assert.Equal(DaggerfallGuildServiceDenial.WrongRegion, away.Denial);
        Assert.True(highRank.Eligible);
        Assert.True(armor.Eligible);
        Assert.Equal(DaggerfallGuildServiceDenial.AlreadyClaimed, claimedArmor.Denial);
        Assert.True(house.Eligible);
        Assert.Equal(DaggerfallGuildServiceDenial.HouseAlreadyClaimed, claimedHouse.Denial);
    }

    [Fact]
    public void Fighters_preserve_donor_fixed_point_reward_and_repair_formulas()
    {
        Assert.Equal(100, DaggerfallConcreteGuildPolicy.FightersReward(100, 0));
        Assert.Equal(189, DaggerfallConcreteGuildPolicy.FightersReward(100, 9));
        Assert.Equal(100, DaggerfallConcreteGuildPolicy.FightersRepairCost(100, 0));
        Assert.Equal(9, DaggerfallConcreteGuildPolicy.FightersRepairCost(100, 9));
    }
}
