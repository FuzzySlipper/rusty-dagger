using System.Text.Json;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTransformationInfectionTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void All_variants_keep_strict_days_and_real_terminal_warning_order(int variant)
    {
        var kind = (DaggerfallInfectionKind)variant;
        var state = new DaggerfallInfectionState(kind, 100, 3, DaggerfallInfectionStage.Incubating);
        Assert.Equal(state, DaggerfallTransformationInfectionPolicy.Advance(state, 100));
        state = DaggerfallTransformationInfectionPolicy.Advance(state, 101);
        Assert.Equal(DaggerfallInfectionStage.WarningPending, state.Stage);
        Assert.Equal(state, DaggerfallTransformationInfectionPolicy.Advance(state, 120));
        string warning = kind == DaggerfallInfectionKind.Vampire ? "ANIM0004.VID" : "ANIM0002.VID";
        state = DaggerfallTransformationInfectionPolicy.Advance(state, 103, new(warning, VideoRealizationFactKind.Completed, null));
        Assert.Equal(DaggerfallInfectionStage.Warned, DaggerfallTransformationInfectionPolicy.Advance(state, 103).Stage);
        state = DaggerfallTransformationInfectionPolicy.Advance(state, 104);
        if (kind == DaggerfallInfectionKind.Vampire)
        {
            Assert.Equal(DaggerfallInfectionStage.DeathPending, state.Stage);
            state = DaggerfallTransformationInfectionPolicy.Advance(state, 104, new("ANIM0012.VID", VideoRealizationFactKind.Skipped, null));
        }
        Assert.Equal(DaggerfallInfectionStage.ReadyForTransformation, state.Stage);
        Assert.Equal(3, state.InfectionRegion);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void Session_elapsed_save_restore_retains_ready_output_until_consumer_accepts(int variant)
    {
        using Fixture f = new(videos: false); var s = f.Session;
        var kind = (DaggerfallInfectionKind)variant;
        Assert.Equal(DaggerfallInfectionAdmission.Started, s.InflictTransformationInfection(Exposure("infection", kind)));
        s.AdvanceElapsedTime(4 * 86400);
        Assert.Equal(DaggerfallInfectionStage.WarningPending, State(s).Stage);
        using var pending = f.Restore(s.CaptureSave());
        pending.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(DaggerfallInfectionStage.Warned, State(pending).Stage);
        Assert.Empty(pending.Infections.Ready);
        pending.Update(new ProductUpdate(OuterUpdate(2), []));
        if (kind == DaggerfallInfectionKind.Vampire)
        {
            Assert.Equal(DaggerfallInfectionStage.ReadyForTransformation, State(pending).Stage);
            pending.Update(new ProductUpdate(OuterUpdate(3), []));
        }
        if (kind != DaggerfallInfectionKind.Vampire)
        {
            Assert.Empty(pending.Infections.Ready);
            var racial = Assert.IsType<DaggerfallRacialOverrideView>(pending.State.RacialOverrides.Current);
            Assert.Equal(kind == DaggerfallInfectionKind.Werewolf ? DaggerfallRacialKind.Werewolf : DaggerfallRacialKind.Wereboar, racial.State.Kind);
            using var permanent = f.Restore(pending.CaptureSave());
            Assert.Equal(racial, permanent.State.RacialOverrides.Current);
            Assert.Equal(DaggerfallInfectionCleanup.Consumed, Assert.Single(DaggerfallSavePayload.Read(permanent.CaptureSave()).Infections.LastOutcomes).Outcome);
            return;
        }
        var ready = Assert.Single(pending.Infections.Ready);
        Assert.Equal(kind, ready.Kind); Assert.Equal(3, ready.InfectionRegion);
        Assert.Contains("unavailable", State(pending).Unavailable);
        using var waiting = f.Restore(pending.CaptureSave());
        Assert.Equal(ready, Assert.Single(waiting.Infections.Ready));
        Consumer consumer = new(); waiting.Infections.BindConsumer(consumer);
        waiting.Update(new ProductUpdate(OuterUpdate(4), []));
        Assert.Single(waiting.State.Effects.Active);
        Assert.Equal("Permanent dependencies not admitted.", State(waiting).Unavailable);
        consumer.Accept = true;
        waiting.Update(new ProductUpdate(OuterUpdate(5), []));
        Assert.Empty(waiting.State.Effects.Active);
        var terminal = Assert.Single(DaggerfallSavePayload.Read(waiting.CaptureSave()).Infections.LastOutcomes);
        Assert.Equal(DaggerfallInfectionCleanup.Consumed, terminal.Outcome);
        Assert.Equal(ready, consumer.Last);
        using var consumed = f.Restore(waiting.CaptureSave());
        Assert.Empty(consumed.Infections.Ready);
        Assert.Equal(terminal, Assert.Single(DaggerfallSavePayload.Read(consumed.CaptureSave()).Infections.LastOutcomes));
        Assert.Equal(DaggerfallInfectionAdmission.RacialOverride, waiting.InflictTransformationInfection(Exposure("after", kind)));
    }

    [Fact]
    public void Duplicate_and_opposing_lycans_reject_but_vampire_can_coexist_until_permanent_consumption()
    {
        using Fixture f = new(videos: false); var s = f.Session;
        Assert.Equal(DaggerfallInfectionAdmission.TargetUnavailable, s.InflictTransformationInfection(Exposure("enemy", DaggerfallInfectionKind.Werewolf) with { TargetId = 2000 }));
        Assert.Equal(DaggerfallInfectionAdmission.Started, s.InflictTransformationInfection(Exposure("wolf", DaggerfallInfectionKind.Werewolf)));
        Assert.Equal(DaggerfallInfectionAdmission.Incumbent, s.InflictTransformationInfection(Exposure("boar", DaggerfallInfectionKind.Wereboar)));
        Assert.Equal(DaggerfallInfectionAdmission.Started, s.InflictTransformationInfection(Exposure("vamp", DaggerfallInfectionKind.Vampire)));
        Assert.Equal(DaggerfallInfectionAdmission.Incumbent, s.InflictTransformationInfection(Exposure("vamp2", DaggerfallInfectionKind.Vampire)));
        Assert.Equal(2, s.CureAllDiseases());
        Assert.Empty(s.State.Effects.Active);
        Assert.All(DaggerfallSavePayload.Read(s.CaptureSave()).Infections.LastOutcomes, value => Assert.Equal(DaggerfallInfectionCleanup.Cured, value.Outcome));
        using var cured = f.Restore(s.CaptureSave()); Assert.Empty(cured.Infections.Ready);
    }

    [Fact]
    public void Missing_media_is_durable_unavailable_cureable_and_never_reports_ready()
    {
        using Fixture f = new(videos: true); var s = f.Session;
        s.InflictTransformationInfection(Exposure("vamp", DaggerfallInfectionKind.Vampire));
        s.AdvanceElapsedTime(8 * 86400);
        s.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal(DaggerfallInfectionStage.WarningPending, State(s).Stage);
        Assert.Contains("unavailable", State(s).Unavailable, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(s.Infections.Ready);
        using var restored = f.Restore(s.CaptureSave()); Assert.Equal(State(s), State(restored));
        Assert.True(restored.Infections.Retry("vamp"));
        restored.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.NotNull(State(restored).Unavailable);
        Assert.Equal(1, restored.CureAllDiseases());
        Assert.Equal(DaggerfallInfectionCleanup.Cured, Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).Infections.LastOutcomes).Outcome);
    }

    [Fact]
    public void Cancellation_and_malformed_elapsed_state_have_explicit_current_save_behavior()
    {
        using Fixture f = new(videos: false); var s = f.Session;
        s.InflictTransformationInfection(Exposure("boar", DaggerfallInfectionKind.Wereboar));
        var save = DaggerfallSavePayload.Read(s.CaptureSave());
        var active = Assert.Single(save.ActiveEffects);
        var wrong = new DaggerfallInfectionState(DaggerfallInfectionKind.Vampire, State(s).StartingDay, 3, DaggerfallInfectionStage.DeathPending);
        Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Encode(save with { ActiveEffects = [active with { State = JsonSerializer.SerializeToElement(wrong, DaggerfallSaveJsonContext.Default.DaggerfallInfectionState) }] }));
        Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Encode(save with { ActiveEffects = [active with { CasterId = 2000 }] }));
        Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Encode(save with { ActiveEffects = [active with { ItemId = 123 }] }));
        Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Encode(save with { ActiveEffects = [active with { BundleKind = DaggerfallEffectBundleKind.HeldMagicItem }] }));
        var stuck = State(s) with { Unavailable = "Impossible incubating failure." };
        Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Encode(save with { ActiveEffects = [active with { State = JsonSerializer.SerializeToElement(stuck, DaggerfallSaveJsonContext.Default.DaggerfallInfectionState) }] }));
        Assert.True(s.State.Effects.Cancel(Assert.Single(s.State.Effects.Active).Context.Instance));
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(DaggerfallInfectionCleanup.Cancelled, Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).Infections.LastOutcomes).Outcome);
    }

    [Fact]
    public void Admitted_cinematic_results_and_skip_drive_stages_and_cure_releases_only_owned_playback()
    {
        var media = new DaggerfallCinematicPresentationTests.Harness(infections: true);
        using Fixture f = new(videos: true, media); var s = f.Session;
        s.InflictTransformationInfection(Exposure("vamp", DaggerfallInfectionKind.Vampire));
        s.AdvanceElapsedTime(4 * 86400);
        s.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Equal("ANIM0004.VID", s.Cinematics!.ActiveSource);
        var savedPending = s.CaptureSave();
        media.Facts.Add(new(VideoRealizationFactKind.Completed, 1, new(1), VideoFailureCode.None));
        s.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(DaggerfallInfectionStage.Warned, State(s).Stage);
        s.Update(new ProductUpdate(OuterUpdate(3), []));
        Assert.Equal("ANIM0012.VID", s.Cinematics.ActiveSource);
        s.Update(new ProductUpdate(OuterUpdate(4), [Ui("{\"action\":\"cinematic-skip\"}")]));
        s.Update(new ProductUpdate(OuterUpdate(5), []));
        Assert.Equal(DaggerfallInfectionStage.ReadyForTransformation, State(s).Stage);
        Assert.Single(s.Infections.Ready);
        Assert.Equal(1, s.CureAllDiseases());
        using var restored = f.Restore(savedPending);
        restored.Update(new ProductUpdate(OuterUpdate(6), []));
        Assert.Equal("ANIM0004.VID", restored.Cinematics!.ActiveSource);
        int stops = media.Stops;
        Assert.Equal(1, restored.CureAllDiseases());
        Assert.Equal(stops + 1, media.Stops);
        Assert.Null(restored.Cinematics.ActiveSource);
        restored.Cinematics.Play("ANIM0011.VID");
        restored.Update(new ProductUpdate(OuterUpdate(7), []));
        Assert.Equal("ANIM0011.VID", restored.Cinematics.ActiveSource);
    }

    [Fact]
    public void Site_transition_and_restore_keep_original_region_and_uncompleted_milestones()
    {
        using Fixture f = new(videos: false, twoSites: true); var s = f.Session;
        s.InflictTransformationInfection(Exposure("vamp", DaggerfallInfectionKind.Vampire));
        s.AdvanceElapsedTime(86400);
        var before = State(s);
        Assert.True(s.TryTransitionTo(f.Destination!.ProfileKey));
        Assert.Equal(before, State(s));
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(before, State(restored));
        Assert.True(restored.TryTransitionTo(f.Composition.StartSite.ProfileKey));
        Assert.Equal(before, State(restored));
    }

    [Fact]
    public void Infection_is_target_owned_after_source_actor_is_banished_and_keeps_historical_provenance()
    {
        using Fixture f = new(videos: false); var s = f.Session;
        s.InflictTransformationInfection(Exposure("hit-infection", DaggerfallInfectionKind.Werewolf) with { CasterId = 2000 });
        Assert.Null(Assert.Single(s.State.Effects.Active).Context.Caster);
        Assert.Equal(2000, State(s).OriginActorId);
        s.BanishActor(2000);
        Assert.Single(s.State.Effects.Active);
        using var restored = f.Restore(s.CaptureSave());
        Assert.Equal(2000, State(restored).OriginActorId);
        Assert.Single(restored.State.Effects.Active);
    }

    private static DaggerfallInfectionExposure Exposure(string instance, DaggerfallInfectionKind kind) => new(instance, "monster.hit", null, 1, kind, 3);
    private static DaggerfallInfectionState State(DaggerfallSession session) => DaggerfallTransformationInfectionPolicy.Read(Assert.Single(session.State.Effects.Active));
    private sealed class Consumer : IDaggerfallTransformationConsumer
    {
        internal bool Accept;
        internal DaggerfallInfectionTransition? Last;
        public bool HasRacialOverride { get; private set; }
        public DaggerfallInfectionConsumption Consume(DaggerfallInfectionTransition transition)
        {
            Last = transition;
            if (!Accept) return new(false, "Permanent dependencies not admitted.");
            HasRacialOverride = true;
            return new(true, null);
        }
    }
    private sealed class Fixture : IDisposable
    {
        internal DaggerfallSessionComposition Composition { get; }
        internal DaggerfallSession Session { get; }
        private readonly DaggerfallCinematicPresentationTests.Harness? _media;
        internal DaggerfallSiteProfile? Destination { get; }
        internal Fixture(bool videos, DaggerfallCinematicPresentationTests.Harness? media = null, bool twoSites = false)
        {
            _media = media;
            if (twoSites) Destination = DaggerfallSiteContent.Read(FullContent(TestData.RepositoryRoot), File.ReadAllBytes(Path.Combine(TestData.RepositoryRoot, "content/worldrpg/payloads/daggerfall.castle-necromoghan.json")), TestPayload.Definitions);
            Composition = new(TestPayload.Definitions, ReadInputs(TestData.RepositoryRoot), DaggerfallTuning.Defaults) { VideosEnabled = videos, CinematicContent = media?.Content, Profiles = new DaggerfallSiteProfiles(Destination is null ? [ReadInputs(TestData.RepositoryRoot)] : [ReadInputs(TestData.RepositoryRoot), Destination]) };
            Session = DaggerfallSession.StartNew(Engine(), Composition);
        }
        private IEngineContext Engine()
        {
            List<string> releases = []; ContentFake content = new(releases); PopulateContent(content, Composition.StartSite); if (Destination is not null) PopulateContent(content, Destination);
            var spatial = SpatialFake.Create(Composition.StartSite.SpatialArtifact.Sha256, releases);
            var engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases), random: RandomMaximum.Create());
            return _media is null ? engine.Context : DaggerfallCinematicPresentationTests.Proxy.Create<IEngineContext>((method, args) => method == "get_Video" ? _media.Video : typeof(IEngineContext).GetMethod(method)!.Invoke(engine.Context, args));
        }
        internal DaggerfallSession Restore(RulesetSavePayload payload) => DaggerfallSession.Restore(Engine(), Composition, payload);
        public void Dispose() => Session.Dispose();
    }
}
