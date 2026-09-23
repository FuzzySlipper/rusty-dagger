using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallGuildMembershipPolicyTests
{
    [Fact]
    public void Admission_uses_reputation_and_two_skill_gate_and_exposes_next_rank_and_privileges()
    {
        DaggerfallSocialState social = Social(Faction(41, 2));
        Dictionary<string, int> skills = new(StringComparer.Ordinal) { ["blade"] = 22, ["axe"] = 0 };
        DaggerfallGuildMembershipPolicy policy = Policy(social, skills,
            Definition(41, privileges: [new("hall", 0), new("master-service", 2)]));

        DaggerfallGuildMembershipView before = policy.Read(41, currentDay: 0);
        Assert.False(before.IsMember);
        Assert.Equal(0, before.NextRankRequirement!.Rank);
        Assert.Equal(22, before.NextRankRequirement.HighSkillMinimum);
        Assert.Empty(before.Privileges);

        DaggerfallGuildMembershipResult oneSkill = policy.Admit(41, currentDay: 0);
        Assert.Equal(DaggerfallGuildMembershipDenial.InsufficientSkills, oneSkill.Denial);
        Assert.False(oneSkill.Applied);

        skills["axe"] = 4;
        social.ChangeFactionReputation(41, -1);
        DaggerfallGuildMembershipResult badReputation = policy.Admit(41, currentDay: 0);
        Assert.Equal(DaggerfallGuildMembershipDenial.InsufficientReputation, badReputation.Denial);

        social.ChangeFactionReputation(41, 1);
        DaggerfallGuildMembershipResult admitted = policy.Admit(41, currentDay: 0);
        Assert.True(admitted.Applied);
        Assert.Equal(DaggerfallGuildMembershipChange.Admitted, admitted.Change);
        Assert.Equal(0, admitted.View.Rank);
        Assert.Contains("hall", admitted.View.Privileges);
        Assert.DoesNotContain("master-service", admitted.View.Privileges);
    }

    [Fact]
    public void Rank_review_waits_for_twenty_eight_days_then_applies_promotion_and_privilege_changes()
    {
        DaggerfallSocialState social = Social(Faction(41, 2));
        Dictionary<string, int> skills = new(StringComparer.Ordinal) { ["blade"] = 22, ["axe"] = 4 };
        DaggerfallGuildPolicyDefinition definition = Definition(41,
            privileges: [new("hall", 0), new("advanced", 1), new("master-service", 2)]);
        DaggerfallGuildMembershipPolicy policy = Policy(social, skills, definition);
        Assert.True(policy.Admit(41, currentDay: 0).Applied);

        social.ChangeFactionReputation(41, 10);
        skills["blade"] = 23;
        skills["axe"] = 5;
        DaggerfallGuildMembershipResult tooSoon = policy.ReviewRank(41, currentDay: 27);
        Assert.Equal(DaggerfallGuildMembershipDenial.ReviewTooSoon, tooSoon.Denial);
        Assert.Equal(0, tooSoon.View.Rank);

        DaggerfallGuildMembershipResult promoted = policy.ReviewRank(41, currentDay: 28);
        Assert.True(promoted.Applied);
        Assert.Equal(DaggerfallGuildMembershipChange.Promoted, promoted.Change);
        Assert.Equal(1, promoted.View.Rank);
        Assert.Equal(28, promoted.View.LastRankChangeDay);
        Assert.Equal(28, promoted.View.DaysUntilReview);
        Assert.Contains("advanced", promoted.View.Privileges);
        Assert.Equal(2, promoted.View.NextRankRequirement!.Rank);

        social.ChangeFactionReputation(41, 10);
        skills["blade"] = 31;
        skills["axe"] = 9;
        DaggerfallGuildMembershipResult promotedAgain = policy.ReviewRank(41, currentDay: 56);
        Assert.Equal(DaggerfallGuildMembershipChange.Promoted, promotedAgain.Change);
        Assert.Equal(2, promotedAgain.View.Rank);
        Assert.Contains("master-service", promotedAgain.View.Privileges);
    }

    [Fact]
    public void Review_expels_bad_standing_and_rejoin_restores_one_canonical_membership()
    {
        DaggerfallSocialState social = Social(Faction(41, 2));
        Dictionary<string, int> skills = new(StringComparer.Ordinal) { ["blade"] = 22, ["axe"] = 4 };
        DaggerfallGuildMembershipPolicy policy = Policy(social, skills,
            Definition(41, privileges: [new("repair", 0)]));
        Assert.True(policy.Admit(41, currentDay: 4).Applied);
        social.ChangeFactionReputation(41, -1);

        DaggerfallGuildMembershipResult expelled = policy.ReviewRank(41, currentDay: 32);
        Assert.Equal(DaggerfallGuildMembershipChange.Expelled, expelled.Change);
        Assert.False(expelled.View.IsMember);
        Assert.False(policy.HasPrivilege(41, "repair"));
        Assert.Empty(social.Capture().Memberships);

        social.ChangeFactionReputation(41, 1);
        DaggerfallGuildMembershipResult rejoined = policy.Rejoin(41, currentDay: 33);
        Assert.True(rejoined.Applied);
        Assert.Equal(DaggerfallGuildMembershipChange.Rejoined, rejoined.Change);
        Assert.Equal(0, rejoined.View.Rank);
        Assert.Equal(33, rejoined.View.LastRankChangeDay);
        Assert.True(policy.HasPrivilege(41, "repair"));
    }

    [Fact]
    public void Guild_group_variant_conflict_is_refused_then_rejoin_uses_the_same_saved_record()
    {
        DaggerfallSocialState social = Social(Faction(41, 2), Faction(17, 2));
        Dictionary<string, int> skills = new(StringComparer.Ordinal) { ["blade"] = 22, ["axe"] = 4 };
        DaggerfallGuildMembershipPolicy policy = Policy(social, skills,
            Definition(41), Definition(17));
        Assert.True(policy.Admit(41, currentDay: 0).Applied);

        DaggerfallGuildMembershipResult conflicting = policy.Admit(17, currentDay: 0);
        Assert.Equal(DaggerfallGuildMembershipDenial.VariantConflict, conflicting.Denial);
        DaggerfallGuildMembershipSave saved = Assert.Single(social.Capture().Memberships);
        Assert.Equal(41, saved.FactionId);
        Assert.Equal(2, saved.GuildGroup);

        Assert.True(policy.Expel(41, currentDay: 1).Applied);
        DaggerfallGuildMembershipResult rejoinedVariant = policy.Rejoin(17, currentDay: 2);
        Assert.Equal(DaggerfallGuildMembershipChange.Rejoined, rejoinedVariant.Change);
        saved = Assert.Single(social.Capture().Memberships);
        Assert.Equal(17, saved.FactionId);
        Assert.Equal(2, saved.GuildGroup);
    }

    [Fact]
    public void Protected_guild_is_demoted_to_rank_zero_instead_of_expelled()
    {
        DaggerfallSocialState social = Social(Faction(42, 3));
        Dictionary<string, int> skills = new(StringComparer.Ordinal) { ["blade"] = 22, ["axe"] = 4 };
        DaggerfallGuildMembershipPolicy policy = Policy(social, skills,
            Definition(42, neverExpels: true));
        Assert.True(policy.Admit(42, currentDay: 0).Applied);
        social.ChangeFactionReputation(42, -1);

        DaggerfallGuildMembershipResult reviewed = policy.ReviewRank(42, currentDay: 28);
        Assert.Equal(DaggerfallGuildMembershipDenial.NoRankChange, reviewed.Denial);
        Assert.False(reviewed.Applied);
        Assert.True(reviewed.View.IsMember);
        Assert.Equal(0, reviewed.View.Rank);
    }

    [Fact]
    public void Saved_rank_recognition_and_review_day_are_read_after_social_restore()
    {
        DaggerfallSocialState social = Social(Faction(41, 2));
        Dictionary<string, int> skills = new(StringComparer.Ordinal) { ["blade"] = 23, ["axe"] = 5 };
        DaggerfallGuildPolicyDefinition definition = Definition(41, privileges: [new("advanced", 1)]);
        DaggerfallGuildMembershipPolicy policy = Policy(social, skills, definition);
        Assert.True(policy.Admit(41, currentDay: 3).Applied);
        social.ChangeGuildRecognition(41, 7);
        social.ChangeFactionReputation(41, 10);
        Assert.Equal(DaggerfallGuildMembershipChange.Promoted, policy.ReviewRank(41, currentDay: 31).Change);

        DaggerfallSocialSave saved = social.Capture();
        DaggerfallSocialState restored = Social(Faction(41, 2));
        restored.Restore(saved);
        DaggerfallGuildMembershipView view = Policy(restored, skills, definition).Read(41, currentDay: 31);
        Assert.True(view.IsMember);
        Assert.Equal(1, view.Rank);
        Assert.Equal(31, view.LastRankChangeDay);
        Assert.Equal(7, view.Recognition);
        Assert.Contains("advanced", view.Privileges);
        Assert.Equal(7, Assert.Single(restored.ReadAffiliations()).Recognition);
    }

    private static DaggerfallGuildMembershipPolicy Policy(
        DaggerfallSocialState social,
        IReadOnlyDictionary<string, int> skills,
        params DaggerfallGuildPolicyDefinition[] definitions) =>
        new(social, skill => skills.GetValueOrDefault(skill), definitions);

    private static DaggerfallGuildPolicyDefinition Definition(
        int factionId,
        bool neverExpels = false,
        IEnumerable<DaggerfallGuildPrivilegeDefinition>? privileges = null) =>
        new(factionId, ["blade", "axe"], neverExpels, privileges);

    private static DaggerfallSocialState Social(params DaggerfallFactionDefinition[] factions) =>
        new(new DaggerfallFactionsSet(
            factions.ToDictionary(faction => faction.Id),
            new Dictionary<int, DaggerfallRegionFactionDefinition>(),
            new Dictionary<string, int>(StringComparer.Ordinal)));

    private static DaggerfallFactionDefinition Faction(int id, int guildGroup) => new(
        id, id, $"Faction {id}", 0, null, [], 2, "Group", -1, 0, 0, 0,
        [], DaggerfallFactionLinkDisposition.Resolved, [], DaggerfallFactionLinkDisposition.Resolved,
        [], -1, -1, 0, "Commoners", guildGroup, $"Guild {guildGroup}", 0, -1, 0, 0, 0, 0);
}
