using WorldRpg.Rulesets.Daggerfall;
using WorldRpg.Rulesets.Daggerfall.Content;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallSocialStateTests
{
    [Fact]
    public void Reputation_propagation_reaction_membership_decay_and_capture_follow_the_admitted_catalog()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallFactionDefinition faction = definitions.Factions.Factions[15]; // Mephala: children, ally/enemy links, guild group, social group.
        DaggerfallFactionDefinition ally = definitions.Factions.Factions[faction.Allies.First()];
        DaggerfallFactionDefinition enemy = definitions.Factions.Factions[faction.Enemies.First()];
        DaggerfallSocialState social = new(definitions.Factions);

        int factionBefore = social.FactionReputation(faction.Id);
        int allyBefore = social.FactionReputation(ally.Id);
        int enemyBefore = social.FactionReputation(enemy.Id);
        _ = social.ChangeFactionReputation(faction.Id, 10, DaggerfallFactionReputationChange.Propagate);
        _ = social.ChangePersonalReputation(faction.SocialGroup, 7);
        _ = social.ChangeRegionalReputation(0, -4);

        Assert.Equal(factionBefore + 10, social.FactionReputation(faction.Id));
        // Both linked records also live under Oblivion, so the donor first applies the relation
        // adjustment and then the root hierarchy half-adjustment.
        Assert.Equal(allyBefore + 10, social.FactionReputation(ally.Id));
        Assert.Equal(enemyBefore, social.FactionReputation(enemy.Id));
        Assert.Equal(factionBefore + 17, social.ReactionForFaction(faction.Id).Value);
        DaggerfallNpc unaffiliated = new(9001, DaggerfallNpcKind.Static, "unaffiliated-social-test",
            new DaggerfallNpcSite(0, "Daggerfall", "social-test"),
            new DaggerfallNpcAppearance("Breton", "Female", 0, 0, 0, 0), "talker", ["talk"],
            DaggerfallNpcPresence.Hidden, null, null, null);
        Assert.Equal(new DaggerfallFactionReaction(0, 0, 0), social.ReactionForNpc(unaffiliated));
        Assert.Equal(-4, social.RegionalReputation(0));

        DaggerfallGuildMembership joined = social.JoinGuild(faction.Id, currentDay: 8);
        Assert.Equal(0, joined.Rank);
        Assert.True(social.GuildEligibility(faction.Id).Meets(0, -100));
        _ = social.PromoteGuild(faction.Id, currentDay: 12);
        Assert.Equal(2, social.PromoteGuild(faction.Id, currentDay: 13).Rank);
        Assert.Equal(3, social.ChangeGuildRecognition(faction.Id, 3).NotedByGuild);
        Assert.Throws<InvalidOperationException>(() => social.JoinGuild(17, currentDay: 12)); // Same donor guild group, another faction variant.

        social.AdvanceElapsedMinutes(0, DaggerfallSocialState.NormalizeIntervalMinutes);
        Assert.Equal(factionBefore + 9, social.FactionReputation(faction.Id));
        Assert.Equal(-3, social.RegionalReputation(0));
        Assert.Equal(7, social.PersonalReputation(faction.SocialGroup)); // Player social-group standing does not normalize in the donor.

        DaggerfallSocialSave captured = social.Capture();
        DaggerfallSocialState restored = new(definitions.Factions);
        restored.Restore(captured);
        Assert.Equal(social.ReactionForFaction(faction.Id), restored.ReactionForFaction(faction.Id));
        Assert.Equal(social.GuildEligibility(faction.Id), restored.GuildEligibility(faction.Id));
        Assert.True(restored.ExpelGuild(faction.Id));
        Assert.False(restored.GuildEligibility(faction.Id).IsMember);
    }

    [Fact]
    public void Propagation_preserves_knightly_temple_dark_brotherhood_and_questor_donor_exceptions()
    {
        DaggerfallDefinitions definitions = Definitions();

        DaggerfallSocialState knightly = new(definitions.Factions);
        int orderBefore = knightly.FactionReputation(91);
        int nobleRootBefore = knightly.FactionReputation(25);
        int genericOrderBefore = knightly.FactionReputation(844);
        int smithBefore = knightly.FactionReputation(845);
        int questorBefore = knightly.FactionReputation(846);
        _ = knightly.ChangeFactionReputation(91, 10, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(orderBefore + 10, knightly.FactionReputation(91));
        Assert.Equal(nobleRootBefore, knightly.FactionReputation(25));
        Assert.Equal(genericOrderBefore + 10, knightly.FactionReputation(844));
        Assert.Equal(smithBefore, knightly.FactionReputation(845));
        Assert.Equal(questorBefore, knightly.FactionReputation(846));

        DaggerfallSocialState temple = new(definitions.Factions);
        int genericTempleBefore = temple.FactionReputation(450);
        int templeQuestorBefore = temple.FactionReputation(240);
        _ = temple.ChangeFactionReputation(25, 10, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(genericTempleBefore + 10, temple.FactionReputation(450));
        Assert.Equal(templeQuestorBefore + 10, temple.FactionReputation(240));

        DaggerfallSocialState brotherhood = new(definitions.Factions);
        int parentBefore = brotherhood.FactionReputation(356);
        int brotherhoodBefore = brotherhood.FactionReputation(108);
        int questBefore = brotherhood.FactionReputation(807);
        _ = brotherhood.ChangeFactionReputation(839, 10, DaggerfallFactionReputationChange.Propagate);
        Assert.Equal(parentBefore, brotherhood.FactionReputation(356));
        Assert.Equal(brotherhoodBefore + 5, brotherhood.FactionReputation(108));
        Assert.Equal(questBefore + 10, brotherhood.FactionReputation(807));

        _ = temple.ChangePersonalReputation(0, 100_000);
        Assert.Equal(short.MaxValue, temple.PersonalReputation(0));
    }

    [Fact]
    public void Propagation_interleaves_relation_slots_before_clamping_a_repeated_link()
    {
        DaggerfallFactionsSet catalog = new(
            new Dictionary<int, DaggerfallFactionDefinition>
            {
                [1] = Faction(1, [2, 2], [2, 2]),
                [2] = Faction(2, [], []),
            },
            new Dictionary<int, DaggerfallRegionFactionDefinition> { [0] = new(0, [], DaggerfallRegionFactionDisposition.Unclaimed) },
            new Dictionary<string, int>());
        DaggerfallSocialState social = new(catalog);
        _ = social.ChangeFactionReputation(2, 99);

        _ = social.ChangeFactionReputation(1, 10, DaggerfallFactionReputationChange.Propagate);

        // DFU's source loop is ally[0], enemy[0], ally[1], enemy[1]. Grouping the lists would
        // clamp both positives before either negative and leave 90 instead of the donor's 95.
        Assert.Equal(95, social.FactionReputation(2));
    }

    [Fact]
    public void Signed_calendar_minutes_normalize_on_floor_boundaries_and_memberships_retain_pre_start_days()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallSocialState social = new(definitions.Factions);
        _ = social.ChangeFactionReputation(15, 10);

        social.AdvanceElapsedMinutes(-1, 0);
        Assert.Equal(9, social.FactionReputation(15));
        social.AdvanceElapsedMinutes(-DaggerfallSocialState.NormalizeIntervalMinutes - 1, -DaggerfallSocialState.NormalizeIntervalMinutes);
        Assert.Equal(8, social.FactionReputation(15));
        Assert.Throws<ArgumentOutOfRangeException>(() => social.AdvanceElapsedMinutes(0, -1));

        DaggerfallGuildMembership joined = social.JoinGuild(15, currentDay: -3);
        Assert.Equal(-3, joined.LastRankChangeDay);
        DaggerfallSocialState restored = new(definitions.Factions);
        restored.Restore(social.Capture());
        Assert.Equal(-3, Assert.Single(restored.Capture().Memberships).LastRankChangeDay);
    }

    [Fact]
    public void Save_rejects_unknown_social_owner_and_invalid_membership_variant()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallSocialSave unknownFaction = new([new DaggerfallFactionReputationSave(999999, 0)], [], [], []);
        Assert.Throws<ArgumentException>(() => unknownFaction.Validate(definitions.Factions));
        DaggerfallSocialSave incompleteFactions = new([new DaggerfallFactionReputationSave(15, 0)], [], [], []);
        Assert.Throws<ArgumentException>(() => incompleteFactions.Validate(definitions.Factions));

        DaggerfallSocialSave wrongGuildVariant = new([], [], [], [new DaggerfallGuildMembershipSave(2, 41, 0, 0, 0)]);
        Assert.Throws<ArgumentException>(() => wrongGuildVariant.Validate(definitions.Factions));
    }


    private static DaggerfallFactionDefinition Faction(int id, IReadOnlyList<int> allies, IReadOnlyList<int> enemies) => new(
        id, id, $"Faction {id}", 0, null, [], 2, "Group", -1, 0, 0, 0,
        allies, DaggerfallFactionLinkDisposition.Resolved, enemies, DaggerfallFactionLinkDisposition.Resolved,
        [], -1, -1, 0, "Commoners", -1, "None", 0, -1, 0, 0, 0, 0);

    private static DaggerfallDefinitions Definitions()
    {
        string root = FindRepositoryRoot();
        return DaggerfallBaseContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.base.json")));
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "content/worldrpg/payloads/daggerfall.base.json"))) return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Could not locate the Rusty Dagger repository root.");
    }
}
