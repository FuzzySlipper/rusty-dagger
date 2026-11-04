using System.Numerics;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>A hurt trap the player sets off reaches the bodies close by.</summary>
public sealed class DungeonTrapSessionTests
{
    private const long Enemy = 2000;

    private static DaggerfallDungeonActionDefinition Action(DaggerfallDungeonActionFlag flag) =>
        new("trap", 1, (uint)DaggerfallDungeonTriggerFlag.Direct, (byte)flag, 0, 0, 2, -1, null, IsFlat: true);

    private static double Health(DaggerfallSession session) => session.State.Actors.Get(Enemy).Stats.GetTrack(TrackId.Parse("health")).Current;

    private static DaggerfallSession Standing(float away)
    {
        List<string> releases = [];
        (DaggerfallSession session, _, _) = VisibleEnemySession(releases, distance: 5d);
        ActorState enemy = session.State.Actors.Get(Enemy);
        Track health = enemy.Stats.GetTrack(TrackId.Parse("health"));
        health.Maximum.BaseValue = 1000;
        health.SetCurrent(1000);
        WorldPoint player = session.State.PlayerControl.Position!.Value;
        enemy.ApplyPose(new ActorPose(WorldPoint.From(player.ToVector() + new Vector3(away, 0f, 0f)), 0f));
        return session;
    }

    [Fact]
    public void A_hurt_trap_catches_a_body_close_by()
    {
        using DaggerfallSession session = Standing(1f);

        session.SpreadHurt(Action(DaggerfallDungeonActionFlag.Hurt22), 1);

        Assert.True(Health(session) < 1000d);
    }

    [Fact]
    public void A_body_out_of_reach_is_untouched_and_only_a_hurt_spreads()
    {
        using DaggerfallSession far = Standing(DaggerfallHazardReach + 1f);
        far.SpreadHurt(Action(DaggerfallDungeonActionFlag.Hurt22), 1);
        Assert.Equal(1000d, Health(far));

        using DaggerfallSession near = Standing(1f);
        near.SpreadHurt(Action(DaggerfallDungeonActionFlag.DrainMagicka), 1);
        Assert.Equal(1000d, Health(near));
    }

    private static float DaggerfallHazardReach => DaggerfallTuning.Defaults.Hazards.TrapRadius;
}
