using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class ExteriorBuildingDoorSessionTests
{
    private static readonly SharedFixture<(DaggerfallSiteProfile Exterior, DaggerfallSiteProfile Interior)> Profiles = new(() =>
    {
        string root = TestData.RepositoryRoot;
        ProductContent content = FullContent(root);
        return (ReadProfile(root, content, TestPayload.Definitions, "daggerfall.charing-exterior.json"),
            ReadProfile(root, content, TestPayload.Definitions, "daggerfall.charing-interior-1-1-0.json"));
    });

    [Theory]
    [InlineData("grab")]
    [InlineData("steal")]
    [InlineData("bash")]
    public void Accepted_real_building_door_operation_enters_its_source_interior_and_restores_the_same_door(string mode)
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallSkills.Lockpicking)).BaseValue = 100;
        session.State.Actors.Player.Stats.GetStat(StatId.Parse(DaggerfallMechanicsIds.Strength.Value)).BaseValue = 100;
        if (mode == "grab") session.Doors.Unlock(fixture.DoorId, DaggerfallDoorOperationSource.Player);
        fixture.SetMode(mode);
        fixture.Interact();
        Assert.True(session.ActivationView.Applied, session.ActivationView.Message);
        Assert.Equal(fixture.Interior.ProfileKey, session.Sites.ActiveProfile);
        var incident = Assert.Single(session.State.Crime.Incidents);
        Assert.Equal(mode == "grab" ? WorldRpg.Rulesets.Daggerfall.Crime.DaggerfallCrimeKind.Trespassing
            : WorldRpg.Rulesets.Daggerfall.Crime.DaggerfallCrimeKind.BreakingAndEntering, incident.Crime);
        Assert.Equal(mode == "bash", incident.Reported); // Minimum random notices bashing, never lockpicking.
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.Single(restored.State.Crime.Incidents);
        Assert.True(restored.TryTransitionTo(fixture.Exterior.ProfileKey));
        Assert.Equal(0, restored.Doors.Read(fixture.DoorId).LockValue);
        Assert.Equal(484, restored.Doors.All.Count());
    }

    [Fact]
    public void Prepared_open_enters_only_after_the_real_exterior_lock_level_accepts_it()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        Assert.Equal(4, session.Doors.Read(fixture.DoorId).LockValue);
        fixture.CastOpen();
        fixture.Interact();
        Assert.False(session.ActivationView.Applied);
        Assert.Equal(fixture.Exterior.ProfileKey, session.Sites.ActiveProfile);
        Assert.Equal(4, session.Doors.Read(fixture.DoorId).LockValue);
        Assert.Empty(session.State.Effects.Active);
        session.State.Progression.AdvanceTo(0, 4);
        fixture.CastOpen();
        fixture.Interact();
        Assert.True(session.ActivationView.Applied, session.ActivationView.Message);
        Assert.Equal(fixture.Interior.ProfileKey, session.Sites.ActiveProfile);
        Assert.Empty(session.State.Effects.Active);
    }

    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSiteProfile Exterior { get; } = Profiles.Value.Exterior;
        internal DaggerfallSiteProfile Interior { get; } = Profiles.Value.Interior;
        internal DaggerfallRdbDoorId DoorId { get; }
        internal DaggerfallSession Session { get; }
        private readonly DaggerfallSessionComposition _composition;
        private SpatialFake _spatial = null!;
        private ulong _step;

        internal Fixture()
        {
            DaggerfallRdbDoorDefinition door = Assert.Single(Exterior.Doors,
                value => value.ExteriorBuilding == new DaggerfallSiteBuildingId(1, 1, 0));
            DoorId = door.Id;
            Assert.True(door.BoundsMin.X < door.BoundsMax.X && door.BoundsMin.Y < door.BoundsMax.Y && door.BoundsMin.Z < door.BoundsMax.Z);
            _composition = new(TestPayload.Definitions, Exterior, DaggerfallTuning.Defaults)
            {
                Profiles = new DaggerfallSiteProfiles([Exterior, Interior]),
            };
            Session = DaggerfallSession.StartNew(Engine().Context, _composition);
        }

        private EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases);
            PopulateContent(content, Exterior);
            PopulateContent(content, Interior);
            _spatial = SpatialFake.Create(Exterior.SpatialArtifact.Sha256, releases);
            _spatial.KeepPosition = true;
            PerceptionFake perception = PerceptionFake.Create();
            perception.Responder = request => Receipt(request.Targets.Span.ToArray()
                .Select(target => new PerceptionPair(1, target.Entity, 1, 1, PerceptionPairKind.Visible, 1)).ToArray());
            return EngineContextFake.Create(content, _spatial.Service, new AppearanceFake(releases), perception.Service, random: RandomMinimum.Create());
        }

        internal DaggerfallSession Restore(RulesetSavePayload save) => DaggerfallSession.Restore(Engine().Context, _composition, save);

        internal void SetMode(string mode) => Session.Update(new ProductUpdate(OuterUpdate(++_step), [Ui("{\"action\":\"activation-mode\",\"mode\":\"" + mode + "\"}")]));

        internal void CastOpen()
        {
            Session.State.Character.LearnSpell("spell.018");
            Track magicka = Session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka"));
            magicka.Maximum.BaseValue = 10000;
            magicka.SetCurrent(10000);
            Assert.Equal(DaggerfallCastOutcome.Ready, Session.Casting.Ready(1, "spell.018").Outcome);
            Assert.Equal(DaggerfallCastOutcome.Applied, Assert.Single(Session.ReleaseReadySpell(1, Vector3.UnitZ).Bundle!.Results).Outcome);
        }

        internal void Interact()
        {
            DaggerfallDoorView door = Session.Doors.Read(DoorId);
            Session.State.PlayerControl.MoveTo(door.Pose.Translation + Vector3.UnitZ);
            Session.State.PlayerControl.YawRadians = 0;
            Session.State.PlayerControl.PitchRadians = 0;
            _spatial.FloorHit = request => request.Direction.Y < -.5 ? default : new SpatialHit
            {
                Present = true, Kind = SpatialHitKind.Entity, Entity = door.Entity.Value,
                Point = door.Pose.Translation, Distance = 1,
            };
            Session.Update(new ProductUpdate(OuterUpdate(++_step), [Input(InputEventKind.DirectDigital, x: 1, phase: InputPhase.DirectUi, intent: "interact")]));
        }

        public void Dispose() => Session.Dispose();
    }
}
