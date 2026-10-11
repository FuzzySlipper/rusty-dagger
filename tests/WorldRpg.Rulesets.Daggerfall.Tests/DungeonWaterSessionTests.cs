using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Modules.Behavior;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DungeonWaterSessionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Castle_water_reaches_player_swimming_drowning_and_aquatic_enemy_navigation(bool assembled)
    {
        var world = new AssembledWorld();
        var published = ReadProfile(TestData.RepositoryRoot, FullContent(TestData.RepositoryRoot), world.Definitions,
            "daggerfall.castle-necromoghan.json");
        var profile = assembled ? world.Profiles.Require(published.ProfileKey) : published;
        var water = Assert.Single(profile.WaterVolumes);
        var center = (water.Minimum + water.Maximum) * .5f;
        center.Y = water.Maximum.Y - 2;
        List<string> releases = [];
        var content = new ContentFake(releases); PopulateContent(content, profile);
        var spatial = SpatialFake.Create(world.Source.SpatialArtifact.Sha256, releases);
        spatial.KeepPosition = true;
        spatial.MovementFact = request => new(request.Command.Movement.Mode, 1f, true, false, false, false);
        spatial.TriggerResult = request => new(
            new[] { new SpatialTriggerFact(true, water.Trigger, 1, request.Tick, SpatialTriggerCause.Movement) }, request.Tick, request.Cause, 0, 0, 0);
        var perception = PerceptionFake.Create();
        var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
        using var session = DaggerfallSession.StartNew(engine.Context, new(world.Definitions, profile, DaggerfallTuning.Defaults));
        Assert.All(spatial.TriggerRegistrations.Where(request => request.Tag == "water"),
            request => Assert.Matches("^[A-Za-z0-9_.-]+$", request.Scope));
        session.State.PlayerControl.MoveTo(center);
        long fish = session.SpawnActor("slaughterfish", new(new(center.X, center.Y, center.Z + 5), 0));
        var actor = session.State.Actors.Get(fish);
        Assert.NotNull(session.State.Kit.Attacks.ReachOf(fish));
        actor.ApplyPose(new(new(center.X, center.Y, center.Z + 5), 0));
        actor.Actor.Get<DaggerfallEnemyPerceptionMemory>().SetForcedHostile(true);
        perception.Receipt = Receipt(new PerceptionPair((ulong)fish, 1, 5, 1, PerceptionPairKind.Visible, 1));
        session.State.WeaponDrawn = false;
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.True(session.State.Swimming.IsSwimming);
        Assert.True(session.State.Swimming.HeadSubmerged);
        Assert.Equal(water.Trigger, session.State.Swimming.ActiveWaterTrigger);
        Assert.Equal(water.Minimum, spatial.TriggerRows[water.Trigger].Min);
        Assert.Equal(water.Maximum, spatial.TriggerRows[water.Trigger].Max);
        Assert.Contains(water.Trigger, spatial.TriggersAt(actor.Position.ToVector()));
        Assert.True(spatial.StepRequests.Any(request => request.Position == actor.Position.ToVector()
            && request.Command.Movement.Mode == CharacterMovementMode.Swimming),
            $"Fish behavior: {session.LastEnemyBehavior[fish]}; position {actor.Position}; request count {spatial.StepRequests.Count}; pairs {string.Join(";", session.LastEnemyBehavior[fish].Visibility!.Value.Pairs.ToArray())}");
        // Continue the ordinary admitted swimming consequence until the breath pool is exhausted.
        for (ulong step = 2; step < 302 && !session.State.Actors.Player.IsDefeated; step++)
            session.Update(new ProductUpdate(OuterUpdate(step) with { FixedDeltaSeconds = 1 }, []));
        Assert.True(session.State.Actors.Player.IsDefeated);
    }
}
