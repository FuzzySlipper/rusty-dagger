using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTemplePolicyTests
{
    [Fact]
    public void Blessing_table_covers_the_eight_source_deities_and_keeps_Arkay_cure_only()
    {
        Assert.Equal(DaggerfallTempleBlessingTarget.Speed, Target(DaggerfallConcreteGuildCatalog.AkatoshFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.Luck, Target(DaggerfallConcreteGuildCatalog.DibellaFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.Intelligence, Target(DaggerfallConcreteGuildCatalog.JulianosFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.Endurance, Target(DaggerfallConcreteGuildCatalog.KynarethFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.Personality, Target(DaggerfallConcreteGuildCatalog.MaraFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.LegalReputation, Target(DaggerfallConcreteGuildCatalog.StendarrFactionId));
        Assert.Equal(DaggerfallTempleBlessingTarget.Mercantile, Target(DaggerfallConcreteGuildCatalog.ZenitharFactionId));
        Assert.True(DaggerfallTemplePolicy.TryResolveTarget(DaggerfallConcreteGuildCatalog.ArkayFactionId, out DaggerfallTempleBlessingTarget arkay));
        Assert.Equal(DaggerfallTempleBlessingTarget.None, arkay);
        Assert.False(DaggerfallTemplePolicy.TryResolveTarget(999_999, out _));

        static DaggerfallTempleBlessingTarget Target(int deity)
        {
            Assert.True(DaggerfallTemplePolicy.TryResolveTarget(deity, out DaggerfallTempleBlessingTarget target));
            return target;
        }
    }

    [Fact]
    public void Donation_formula_uses_rank_for_magnitude_and_caps_only_duration()
    {
        Assert.Equal(2, DaggerfallTemplePolicy.CalculateTempleBlessing(1, rank: 0));
        Assert.Equal(11, DaggerfallTemplePolicy.CalculateTempleBlessing(1, rank: 9));
        Assert.Equal(1, DaggerfallTemplePolicy.BlessingDurationMinutes(1));
        Assert.Equal(1440, DaggerfallTemplePolicy.BlessingDurationMinutes(1440));
        Assert.Equal(1440, DaggerfallTemplePolicy.BlessingDurationMinutes(ulong.MaxValue));
        Assert.Equal(250, DaggerfallTemplePolicy.ReducedCureCost(250, DaggerfallConcreteGuildCatalog.ZenitharFactionId, 9, member: true));
        Assert.Equal(24, DaggerfallTemplePolicy.ReducedCureCost(250, DaggerfallConcreteGuildCatalog.ArkayFactionId, 9, member: true));
    }

    [Fact]
    public void Source_temple_building_faction_retains_its_deity_parent()
    {
        DaggerfallConcreteGuildDefinition arkay = DaggerfallConcreteGuildCatalog.ForFaction(
            DaggerfallConcreteGuildCatalog.ArkayTempleFactionId);

        Assert.Equal(DaggerfallGuildMembershipKind.TempleDeity, arkay.MembershipKind);
        Assert.Equal(DaggerfallConcreteGuildCatalog.ArkayFactionId, arkay.ParentFactionId);
        Assert.Equal(arkay.FactionId,
            DaggerfallConcreteGuildCatalog.ForDeity(DaggerfallConcreteGuildCatalog.ArkayFactionId).FactionId);
    }

    [Fact]
    public void Regional_blessing_sources_are_live_and_do_not_change_saved_base_reputation()
    {
        DaggerfallSocialState social = new(TestPayload.Definitions.Factions);
        EffectSourceIdentity source = new(new EntityId(1), EffectInstanceId.Parse("temple-test"), 1,
            SourceDefinitionId.Parse("daggerfall.temple-blessing"));
        int before = social.RegionalReputation(17);
        social.SetRegionalReputationSource(17, source, 7);
        Assert.Equal(before + 7, social.RegionalReputation(17));
        _ = social.ChangeRegionalReputation(17, 3);
        Assert.Equal(before + 3 + 7, social.RegionalReputation(17));
        social.RemoveRegionalReputationSource(17, source);
        Assert.Equal(before + 3, social.RegionalReputation(17));
    }
}
