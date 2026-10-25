using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit.Ai;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>Enemy engagement through the live behavior update: contact closes inside reach, classic swings at reach.</summary>
public sealed class ContactEngagementSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_melee_enemy_inside_its_reach_but_outside_the_contact_engage_distance_closes_before_it_swings(bool contact)
    {
        EnemyBehaviorState expected = contact ? EnemyBehaviorState.Chase : EnemyBehaviorState.Attack;
        double reach = Reach();
        // Inside the attack's reach, outside the contact variant's engage distance.
        double distance = reach * (1d + ContactTuning.EngageReachFraction) / 2d;
        List<string> releases = [];
        (DaggerfallSession session, _, _) = VisibleEnemySession(releases, distance, contact: contact);
        using DaggerfallSession disposable = session;

        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.Equal(expected, session.LastEnemyBehavior[2000].State);
        Assert.Equal(!contact, session.State.Actors.Get(2000).Attack.Pending is not null);
    }

    [Fact]
    public void A_contact_melee_enemy_within_its_engage_distance_swings_and_publishes_its_wind_up()
    {
        double distance = Reach() * ContactTuning.EngageReachFraction * .9d;
        List<string> releases = [];
        (DaggerfallSession session, _, _) = VisibleEnemySession(releases, distance, contact: true);
        using DaggerfallSession disposable = session;

        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.Equal(EnemyBehaviorState.Attack, session.LastEnemyBehavior[2000].State);
        Assert.NotNull(session.State.Actors.Get(2000).Attack.Pending);
        using JsonDocument observation = JsonDocument.Parse(session.ReadPlaytestObservation().Message);
        Assert.Equal(FormattableString.Invariant($"EnemyAttackTelegraph:{ContactTuning.EnemyWindUpSeconds:0.###}"),
            observation.RootElement.GetProperty("lastCombatEvent").GetProperty("kind").GetString());
    }

    private static Variants.DaggerfallContactTuning ContactTuning => Variants.DaggerfallContactTuning.Read(
        TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/variant-tuning-payloads/daggerfall.contact.json")));

    private static double Reach()
    {
        List<string> releases = [];
        (DaggerfallSession session, _, _) = VisibleEnemySession(releases);
        using DaggerfallSession disposable = session;
        return session.State.Kit.Attacks.ReachOf(2000) ?? throw new InvalidOperationException("The placed enemy has no attack reach.");
    }
}
