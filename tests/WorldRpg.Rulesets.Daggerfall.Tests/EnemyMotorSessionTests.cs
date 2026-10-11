using System.Numerics;
using System.Reflection;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class EnemyMotorSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Admitted_session_selects_retreat_or_strafe_and_sends_the_direction_to_navigation(bool strafe)
    {
        using var fixture = new Fixture(strafe);
        var session = fixture.Session;
        var actor = session.State.Actors.Get(2008);
        actor.ApplyPose(new(new(0, 0, -2), 0));
        session.State.PlayerControl.MoveTo(Vector3.Zero);
        session.State.PlayerControl.YawRadians = 0;
        session.State.WeaponDrawn = true;
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(true);
        fixture.Perception.Receipt = Receipt(new PerceptionPair(2008, 1, 2, 1, PerceptionPairKind.Visible, 1));
        fixture.Spatial.NavigationRequests.Clear();
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(strafe ? EnemyBehaviorState.Strafe : EnemyBehaviorState.Retreat, session.LastEnemyBehavior[2008].State);
        var request = fixture.Spatial.NavigationRequests.First(value => value.From == new Vector3(0, 0, -2));
        if (strafe) Assert.NotEqual(0, request.Target.X);
        else Assert.True(request.Target.Z < -2);
    }

    [Fact]
    public void Blocked_door_capable_enemy_opens_the_existing_dungeon_door_owner()
    {
        using var fixture = new Fixture(false);
        var session = fixture.Session;
        var door = session.Doors.All.First(value => value.Kind == DaggerfallDoorKind.Normal && value.LockValue == 0
            && value.Motion == DaggerfallDoorMotion.Closed);
        var point = door.Pose.Translation;
        long id = session.SpawnActor("thief", new(new(point.X, point.Y, point.Z + 1), 0));
        session.State.Actors.Get(id).Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(true);
        session.State.PlayerControl.MoveTo(point - Vector3.UnitZ * 3);
        session.State.WeaponDrawn = false;
        fixture.Perception.Receipt = Receipt(new PerceptionPair((ulong)id, 1, 4, 1, PerceptionPairKind.Visible, 1));
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.NotEqual(DaggerfallDoorMotion.Closed, session.Doors.All.Single(value => value.Id == door.Id).Motion);
    }

    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; }
        internal PerceptionFake Perception { get; } = PerceptionFake.Create();
        internal Fixture(bool strafe)
        {
            var profile = ReadInputs(TestData.RepositoryRoot);
            List<string> releases = [];
            var content = new ContentFake(releases); PopulateContent(content, profile);
            Spatial = SpatialFake.Create(profile.SpatialArtifact.Sha256, releases); Spatial.KeepPosition = true;
            var engine = EngineContextFake.Create(content, Spatial.Service, new AppearanceFake(releases), Perception.Service,
                random: MotorRandom.Create(strafe));
            Session = DaggerfallSession.StartNew(engine.Context, new(TestPayload.Definitions, profile, DaggerfallTuning.Defaults));
        }
        public void Dispose() => Session.Dispose();
    }
}

internal class MotorRandom : RandomMinimum
{
    private bool _strafe;
    internal static IRandomService Create(bool strafe)
    {
        var service = Create<IRandomService, MotorRandom>();
        ((MotorRandom)(object)service)._strafe = strafe;
        return service;
    }
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method?.Name == nameof(IRandomService.DrawKeyed) && arguments![0] is KeyedRngRequest request
            && request.ToString().Contains("daggerfall.enemy-motor", StringComparison.Ordinal)
            && request.Key.EndsWith(":strafe", StringComparison.Ordinal)) return new KeyedRngReceipt(_strafe ? 0 : 1);
        return base.Invoke(method, arguments);
    }
}
