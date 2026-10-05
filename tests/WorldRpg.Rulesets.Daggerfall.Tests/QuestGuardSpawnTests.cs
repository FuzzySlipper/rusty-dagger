using System.Numerics;
using Rusty.Engine;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class QuestGuardSpawnTests
{
    [Fact]
    public void Immediate_request_retries_spatial_admission_and_restores_partial_group_once()
    {
        using var f = new Fixture(true);
        f.Spatial.OverlapHit = _ => default(SpatialHit) with { Present = true };
        f.Advance();
        Assert.Empty(Guards(f.Session));
        Assert.Equal(2, Schedule(f.Session).Remaining);
        using var restored = f.Restore();
        f.Advance(restored);
        long first = Assert.Single(Guards(restored));
        using var partial = f.Restore(restored);
        f.Advance(partial);
        Assert.Equal(2, Guards(partial).Length);
        Assert.Contains(first, Guards(partial));
        Assert.Equal(DaggerfallQuestGuardSpawnOutcome.Complete, Schedule(partial).Outcome);
        f.Advance(partial);
        Assert.Equal(2, Guards(partial).Length);
        Assert.Empty(partial.State.Crime.Incidents);
    }

    [Fact]
    public void Normal_request_requires_a_witness_and_retains_real_time_delay_across_save()
    {
        using var unseen = new Fixture(false);
        unseen.Advance();
        Assert.Equal(DaggerfallQuestGuardSpawnOutcome.NoWitness, Schedule(unseen.Session).Outcome);
        Assert.Empty(Guards(unseen.Session));
        using var f = new Fixture(false);
        f.Civilian();
        f.Advance();
        Assert.Equal(5, Schedule(f.Session).DelaySeconds);
        Assert.Empty(Guards(f.Session));
        f.Advance(seconds: 2);
        Assert.Equal(3, Schedule(f.Session).DelaySeconds);
        using var restored = f.Restore();
        f.Advance(restored, 2);
        Assert.Empty(Guards(restored));
        f.Advance(restored, 1);
        Assert.Single(Guards(restored));
        f.Advance(restored);
        Assert.Equal(2, Guards(restored).Length);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Mobile_watchman_conversion_retires_original_without_creating_a_charge(bool immediate)
    {
        using var f = new Fixture(immediate);
        long npc = f.Civilian("guard");
        f.Advance();
        Assert.Single(Guards(f.Session));
        Assert.False(f.Session.State.Actors.TryGet(npc, out _));
        Assert.Equal(DaggerfallQuestGuardSpawnOutcome.Complete, Schedule(f.Session).Outcome);
        Assert.Empty(f.Session.State.Crime.Incidents);
    }

    [Fact]
    public void Delayed_report_rescans_watchmen_and_wilderness_reports_unavailable()
    {
        using var f = new Fixture(false);
        f.Civilian(); f.Advance();
        long watchman = f.Civilian("guard");
        f.Advance(seconds: 5);
        Assert.Single(Guards(f.Session));
        Assert.False(f.Session.State.Actors.TryGet(watchman, out _));
        Assert.Equal(DaggerfallQuestGuardSpawnOutcome.Complete, Schedule(f.Session).Outcome);
        using var wilderness = new Fixture(true);
        wilderness.Session.State.PlayerControl.MoveTo(new Vector3(10000, 2, 10000));
        wilderness.Advance();
        var action = wilderness.Session.State.Quests.Capture().Instances.Single().Tasks.Single();
        Assert.Contains("wilderness", action.OperationState.Single().UnavailableReason);
        Assert.False(action.OperationCompleted.Single());
        Assert.Empty(Guards(wilderness.Session));
    }

    private static long[] Guards(DaggerfallSession s) => s.DefinitionsByActor.Where(x => x.Value.MobileId == 146).Select(x => x.Key).ToArray();
    private static DaggerfallQuestGuardSpawnState Schedule(DaggerfallSession s) => s.State.Quests.Capture().Instances.Single().Tasks.Single().OperationState.Single().GuardSpawn!;

    internal sealed class Fixture : IDisposable
    {
        internal DaggerfallSession Session { get; }
        internal SpatialFake Spatial { get; private set; } = null!;
        private readonly DaggerfallSessionComposition composition;
        private readonly DaggerfallSiteProfile profile;
        internal Fixture(bool immediate, DaggerfallDefinitions? definitions = null)
        {
            definitions ??= QuestWorldAdmissionTests.Definitions(actions: ["spawncityguards" + (immediate ? " immediate" : "")]);
            profile = ReadProfile(TestData.RepositoryRoot, FullContent(TestData.RepositoryRoot), definitions, "daggerfall.charing-exterior.json");
            composition = new(definitions, profile, DaggerfallTuning.Defaults with { Law = new(5, 5, 2, 2, 12.8f, 51.2f, 3.2) }) { Profiles = new([profile]) };
            Session = DaggerfallSession.StartNew(Engine().Context, composition);
            var site = definitions.Locations.Records.Single(x => x.Id == profile.Site);
            Session.State.Quests.Start(new("guards", "world-test.txt", "world-test", DaggerfallQuestLifecycle.Active, null,
                [new("location", DaggerfallQuestResourceBinding.Place(new(site.Region, site.Index)) with { PlaceSelection = new(profile.ProfileKind, site.MapId) })], []));
        }
        internal void Advance(DaggerfallSession? session = null, double seconds = 0)
        {
            session ??= Session;
            session.State.Quests.Advance(session.State.Variables, DaggerfallCalendar.Start, elapsedSeconds: seconds);
        }
        internal long Civilian(string role = "civilian")
        {
            var site = Session.Site.ActiveSite!;
            long id = Session.State.Npcs.RegisterCivilian(new(site.Id.Region, site.Name, ""), new("breton", "Female", 0, 0, 0, 0), role, ["talk"]);
            return Session.MaterializeNpcActor(id, new(WorldPoint.From(Session.State.PlayerControl.Position!.Value.ToVector() + new Vector3(0, 0, -2)), 0));
        }
        internal DaggerfallSession Restore(DaggerfallSession? from = null) => DaggerfallSession.Restore(Engine().Context, composition, (from ?? Session).CaptureSave());
        private EngineContextFake Engine()
        {
            List<string> releases = [];
            ContentFake content = new(releases); PopulateContent(content, profile);
            Spatial = SpatialFake.Create(profile.SpatialArtifact.Sha256, releases); Spatial.KeepPosition = true;
            Spatial.FloorHit = request => request.Direction.Y < 0 ? default(SpatialHit) with
            { Present = true, Point = new(request.Origin.X, 0, request.Origin.Z), Normal = Vector3.UnitY, Converged = true } : default;
            var perception = PerceptionFake.Create();
            perception.Responder = request => Receipt(request.Observers.ToArray().SelectMany(o => request.Targets.ToArray()
                .Select(t => new PerceptionPair(o.Entity, t.Entity, 2, 1, PerceptionPairKind.Visible, 1))).ToArray());
            return EngineContextFake.Create(content, Spatial.Service, new AppearanceFake(releases), perception.Service);
        }
        public void Dispose() => Session.Dispose();
    }
}
