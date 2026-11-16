using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Testing;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// world.goto and world.enter stand the player at a building's door over the real Engine and Charing's shipped
/// collision. A closed door's collider encloses its own point, so the line to it meets the door itself, and that line
/// reaches it: Lord Dunistair's Gemstones' door had been refused from every bearing for it, and world.enter had then
/// entered from wherever the player stood, which leaving returned them to.
/// </summary>
public sealed class DeveloperDoorStandTests
{
    private static readonly DaggerfallSiteBuildingId Gemstones = new(3, 3, 5);

    [Fact]
    public void Goto_reaches_the_gemstones_door_and_leaving_after_enter_lands_on_its_doorstep()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile exterior = Assembled(DaggerfallWorldProfileIds.Exterior(Charing));
        DaggerfallSiteProfile inside = Assembled(DaggerfallWorldProfileIds.Interior(Charing, Gemstones));
        DaggerfallSiteAnchor start = DaggerfallBaseContent.ReadNewGameStart(TestContentFiles.ReadCopy(TestData.PackPayload("daggerfall.town-start")),
            definitions.NewGame, "daggerfall.town-start").Start.Pose!;
        Dictionary<string, ReadOnlyMemory<byte>> files = PublishedUiArt(root).ToDictionary(entry => entry.Path, entry => (ReadOnlyMemory<byte>)entry.Bytes);
        foreach ((string path, ReadOnlyMemory<byte> bytes) in PresentedFiles(root, exterior).Concat(PresentedFiles(root, inside))) files[path] = bytes;
        using EngineTestHost host = EngineTestHost.Create(new EngineTestHostOptions { Content = files });
        DaggerfallSession session = null!;
        host.Call(engine =>
        {
            EngineContextFake context = EngineContextFake.Create(engine.Content, engine.Spatial, new AppearanceFake([]), engine.Perception,
                worldOrigin: engine.WorldOrigin);
            session = DaggerfallSession.StartNew(context.Context,
                new(definitions, exterior, DaggerfallTuning.Defaults) { StartPose = start, Profiles = new([exterior, inside]) });
        });
        try
        {
            System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
            while (!host.Call(_ => session.AwaitArrivalGround().Done))
            {
                Assert.True(waited.Elapsed < TimeSpan.FromSeconds(60), "The exterior ground never stood.");
                Thread.Sleep(5);
            }
            for (ulong step = 1; step <= 3; step++) host.Call(_ => session.Update(new ProductUpdate(OuterUpdate(step), [])));
            IDeveloperPlacementCommands commands = session.Commands<IDeveloperPlacementCommands>();
            Vector3 away = Position(session);
            DaggerfallActivationTarget door = host.Call(_ =>
            {
                DaggerfallDoorView view = session.Sites.Projection.Doors.All.Single(value => session.Sites.Projection.Doors.ExteriorBuildingOf(value.Id) == Gemstones);
                return session.Activation.Module!.InspectTargets().Single(target => target.Entity == view.Entity);
            });
            Vector2 doorway = Planar(door.Position.ToVector());
            Assert.True(Vector2.Distance(Planar(away), doorway) > 10F, "The town start is away from the door.");

            DebugCommandResult reached = host.Call(_ => commands.GoTo($"{door.Identity.Kind}:{door.Identity.Value}"));
            Assert.True(reached.Succeeded, reached.Message);
            Assert.InRange(Vector2.Distance(Planar(Position(session)), doorway), 1.2F, 1.8F);

            // From the town start again, world.enter stands at that door first, so leaving returns to its doorstep.
            DebugCommandResult back = host.Call(_ => commands.Teleport(away.X, away.Y, away.Z, force: false));
            Assert.True(back.Succeeded, back.Message);
            DebugCommandResult entered = host.Call(_ => commands.Enter("Charing", Gemstones.ToString()));
            Assert.True(entered.Succeeded, entered.Message);
            Assert.Equal(DaggerfallWorldProfileIds.Interior(Charing, Gemstones), session.Sites.ActiveProfile);
            Assert.True(host.Call(_ => session.Sites.TryTransitionTo(DaggerfallWorldProfileIds.Exterior(Charing))));
            Assert.Equal(DaggerfallWorldProfileIds.Exterior(Charing), session.Sites.ActiveProfile);
            Assert.InRange(Vector2.Distance(Planar(Position(session)), doorway), 1.2F, 1.8F);
        }
        finally { host.Call(_ => session.Dispose()); }
    }

    private static Vector3 Position(DaggerfallSession session) => session.State.PlayerControl.Position!.Value.ToVector();

    private static Vector2 Planar(Vector3 value) => new(value.X, value.Z);
}
