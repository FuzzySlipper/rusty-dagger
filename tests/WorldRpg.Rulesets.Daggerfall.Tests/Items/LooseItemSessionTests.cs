using System.Numerics;
using System.Text.Json;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Loose items through the session's real callers: a drop from the inventory becomes an upright body tossed ahead of the
/// player; the carry control takes hold of the pile use would reach and drives it, the attack control throws it instead of
/// swinging, and with nothing loose in front it says so; the developer readout names each body's state.
/// </summary>
public sealed class LooseItemSessionTests
{
    [Fact]
    public void A_dropped_item_becomes_an_upright_body_tossed_ahead_that_the_carry_control_holds_and_attack_throws()
    {
        using Fixture f = new();
        DaggerfallSession s = f.Session;
        s.State.PlayerControl.MoveTo(new Vector3(10F, 1.2F, 10F));
        InventoryStack stack = s.State.Inventory.Read().Stacks.First();
        s.PublishInitial();

        f.Submit(new { action = "inventory-drop", revision = f.Engine.PublishedNested("inventory", "revision"), item = $"stack:{stack.Id.Value}", amount = 1 });
        f.Step();

        DynamicsCreateCuboidBodyRequest made = Assert.Single(f.Engine.Dynamics.Cuboids);
        DynamicsBodyProperties body = made.Body.Properties;
        Assert.Equal(new AxisLocks(false, false, false, true, true, true), body.AxisLocks);
        Assert.False(body.Sleeping);
        Assert.Equal(DaggerfallTuning.Defaults.LooseItems.Friction, body.Friction);
        Assert.Equal(new DynamicsSleepThresholds(DaggerfallTuning.Defaults.LooseItems.SleepSpeed, DaggerfallTuning.Defaults.LooseItems.SleepSeconds), body.Sleep);
        Assert.Equal(DaggerfallTuning.Defaults.LooseItems.DropSpeed, body.LinearVelocity.Length(), 4);
        Assert.Equal(0, body.LinearVelocity.Y);
        Assert.Equal(made.Body.HalfExtents.X, made.Body.HalfExtents.Z);
        long pile = Assert.Single(s.LooseItems.Read()).Id;

        // Facing it from a metre back, level with it, the carry control takes hold of it.
        Vector3 toss = Vector3.Normalize(body.LinearVelocity);
        Vector3 at = made.Body.Transform.Translation;
        s.State.PlayerControl.MoveTo(at - toss);
        f.Press("carry");
        Assert.Equal(pile, s.LooseItems.Carried);
        f.Step();
        DynamicsAction drive = Assert.Single(f.Engine.Dynamics.StepReads[^1].Actions.ToArray());
        Assert.True(drive.Wake);

        // Attack throws it rather than swinging.
        f.Press("attack");
        Assert.Null(s.LooseItems.Carried);
        Assert.Null(s.PlayerAttack.Clock.Current);
        DynamicsAction thrown = f.Engine.Dynamics.StepReads[^1].Actions.ToArray()[^1];
        int strength = s.State.Actors.Player.Stats.GetStat(StatId.Parse("strength")).ValueInt;
        Assert.Equal(Math.Min(DaggerfallTuning.Defaults.LooseItems.ThrowImpulsePerStrength * strength, made.Body.Properties.Mass * DaggerfallTuning.Defaults.LooseItems.MaxThrowSpeed),
            thrown.Impulse.Length(), 3);

        Assert.Contains($"pile {pile} awake", s.LooseItems.Describe());
    }

    [Fact]
    public void The_carry_control_with_nothing_loose_in_front_says_so_and_takes_nothing()
    {
        using Fixture f = new();
        f.Session.PublishInitial();
        f.Press("carry");
        Assert.Null(f.Session.LooseItems.Carried);
        Assert.Contains("Nothing loose to carry there.", f.Session.Presentation.Messages);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<string> releases = [];
        private ulong step;

        internal Fixture()
        {
            DaggerfallSiteProfile inputs = ReadInputs(TestData.RepositoryRoot);
            ContentFake content = new(releases);
            PopulateContent(content, inputs);
            SpatialFake spatial = SpatialFake.Create(inputs.Geometry.Spatial[0].Sha256, releases);
            spatial.KeepPosition = true;
            spatial.FloorHit = request => request.Direction.Y >= -.5F || !spatial.ContentResidencyRequests.Any(admission => admission.Admitted.Length > 0) ? default
                : new SpatialHit { Present = true, Point = request.Origin with { Y = .25F }, Normal = Vector3.UnitY };
            PerceptionFake perception = PerceptionFake.Create();
            perception.Responder = request => Receipt([.. request.Targets.Span.ToArray().Select(target =>
                new PerceptionPair(1, target.Entity, 1d, 1d, PerceptionPairKind.Visible, 1d))]);
            Engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), perception.Service);
            Session = DaggerfallSession.StartNew(Engine.Context, new(TestPayload.Definitions, inputs, DaggerfallTuning.Defaults));
        }

        internal EngineContextFake Engine { get; }
        internal DaggerfallSession Session { get; }

        internal void Submit(object action) => Session.Update(new ProductUpdate(OuterUpdate(++step), [Ui(JsonSerializer.Serialize(action))]));

        internal void Step() => Session.Update(new ProductUpdate(OuterUpdate(++step), []));

        internal void Press(string control) => Session.Update(new ProductUpdate(OuterUpdate(++step),
            [Input(InputEventKind.MappedDigital, InputEdge.Pressed, x: 1, phase: InputPhase.Pressed, intent: control)]));

        public void Dispose() => Session.Dispose();
    }
}
