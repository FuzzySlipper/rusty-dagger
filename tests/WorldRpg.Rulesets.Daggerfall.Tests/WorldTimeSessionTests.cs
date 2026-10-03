using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>World time in the session: stamina recovery, reputation normalization and holiday announcements.</summary>
public sealed class WorldTimeSessionTests
{
    [Fact]
    public void Session_fixed_steps_restore_exhausted_player_stamina()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        using DaggerfallSession session = DaggerfallSession.StartNew(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults));
        var stamina = Rusty.Engine.Mechanics.TrackId.Parse("stamina");
        session.State.Actors.Player.Stats.GetTrack(stamina).SetCurrent(0);

        for (int step = 0; step < 8; step++) session.Update(new ProductUpdateState(.125f));

        Assert.Equal(5d, session.State.Actors.Player.Stats.GetTrack(stamina).Current);
    }

    [Fact]
    public void Elapsed_calendar_time_normalizes_social_faction_and_regional_reputation_once_per_112_days()
    {
        using DaggerfallSession session = FreshSession();
        const int factionId = 15;
        int factionBefore = session.State.Social.FactionReputation(factionId);
        _ = session.State.Social.ChangeFactionReputation(factionId, 10);
        _ = session.State.Social.ChangeRegionalReputation(0, -4);

        DaggerfallCalendarAdvance elapsed = session.AdvanceElapsedTime(DaggerfallSocialState.NormalizeIntervalMinutes * 60L);

        Assert.Equal(DaggerfallSocialState.NormalizeIntervalMinutes * 60L, elapsed.AppliedSeconds);
        Assert.Equal(factionBefore + 9, session.State.Social.FactionReputation(factionId));
        Assert.Equal(-3, session.State.Social.RegionalReputation(0));
    }

    /// <summary>
    /// The donor's holiday announcement through the session: restoring onto a kept holiday at a
    /// settlement announces once on the first playing update, and a dungeon session never announces.
    /// The donor shows text 8349 + holidayId on entering a city; the session observes the same entry
    /// on its first tick, the way the donor also announces after loading.
    /// </summary>
    [Fact]
    public void Restoring_onto_a_holiday_at_a_settlement_announces_once()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        // Day 103 of the year is the eighteenth holiday, kept in region 17 alone; Charing is its city.
        DaggerfallSavePayload baseline = CapturedSave(root);
        DaggerfallSavePayload saved = baseline with
        {
            Calendar = new DaggerfallCalendarSave(406, 3, 12, 12, 0, 0, 0d),
            Site = new DaggerfallSiteSave(new DaggerfallSiteIdSave(17, 4), null, []),
            RegionalPrices = baseline.RegionalPrices with { LastAdvancedDay = new DaggerfallCalendar(406, 3, 12, 12, 0, 0).DayNumber },
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.Restore(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(saved));

        Assert.Null(session.HolidayAnnouncement);
        session.Update(new ProductUpdate(OuterUpdate(1), []));

        Assert.NotNull(session.HolidayAnnouncement);
        Assert.Equal(18, session.HolidayAnnouncement.HolidayId);
        Assert.Equal(new DaggerfallTextKey(DaggerfallTextKind.Resource, "8367"), session.HolidayAnnouncement.TextKey);
        string announced = session.Presentation.LastOutcome;
        Assert.Contains("Day of the Dead", announced, StringComparison.Ordinal);
        session.Update(new ProductUpdate(OuterUpdate(2), []));
        Assert.Equal(announced, session.Presentation.LastOutcome);
        Assert.Equal(18, session.HolidayAnnouncement.HolidayId);
    }

    /// <summary>
    /// The clock crossing midnight into a kept holiday announces at a settlement, once, through the
    /// existing outcome line. The donor's entry-only check cannot observe this; the product's
    /// continuous clock can, so the announcement follows the date rather than only the border.
    /// </summary>
    [Fact]
    public void Crossing_midnight_into_a_holiday_announces()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        // An hour before midnight on the eve of region 17's eighteenth holiday, at its city.
        DaggerfallSavePayload baseline = CapturedSave(root);
        DaggerfallSavePayload saved = baseline with
        {
            Calendar = new DaggerfallCalendarSave(406, 3, 11, 23, 0, 0, 0d),
            Site = new DaggerfallSiteSave(new DaggerfallSiteIdSave(17, 4), null, []),
            RegionalPrices = baseline.RegionalPrices with { LastAdvancedDay = new DaggerfallCalendar(406, 3, 11, 23, 0, 0).DayNumber },
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession session = DaggerfallSession.Restore(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(saved));
        session.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Null(session.HolidayAnnouncement);

        // Two admitted hours cross midnight into the holiday.
        session.Update(new ProductUpdate(FactsWithDelta(7200d), []));
        Assert.Equal(18, session.HolidayAnnouncement?.HolidayId);
        Assert.Contains("Day of the Dead", session.Presentation.LastOutcome, StringComparison.Ordinal);
    }

    [Fact]
    public void A_dungeon_session_never_announces()
    {
        string root = TestData.RepositoryRoot;
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        DaggerfallSiteProfile inputs = ReadInputs(root);
        DaggerfallSavePayload baseline = CapturedSave(root);
        DaggerfallSavePayload saved = baseline with
        {
            Calendar = new DaggerfallCalendarSave(406, 3, 12, 12, 0, 0, 0d),
            Site = new DaggerfallSiteSave(new DaggerfallSiteIdSave(17, 179), null, []),
            RegionalPrices = baseline.RegionalPrices with { LastAdvancedDay = new DaggerfallCalendar(406, 3, 12, 12, 0, 0).DayNumber },
        };
        List<string> releases = [];
        ContentFake content = new(releases);
        PopulateContent(content, inputs);
        SpatialFake spatial = SpatialFake.Create(inputs.SpatialArtifact.Sha256, releases);
        EngineContextFake engine = EngineContextFake.Create(content, spatial.Service, new AppearanceFake(releases));
        ResolvedCompositionIdentity identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity;
        using DaggerfallSession restored = DaggerfallSession.Restore(engine.Context, new(definitions, inputs, DaggerfallTuning.Defaults, identity), DaggerfallSavePayload.Encode(saved));
        restored.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Null(restored.HolidayAnnouncement);

        // The fresh bundle session starts in a dungeon on an ordinary day: silent too.
        using DaggerfallSession fresh = FreshSession();
        fresh.Update(new ProductUpdate(OuterUpdate(1), []));
        Assert.Null(fresh.HolidayAnnouncement);
    }

    private static ProductUpdateFacts FactsWithDelta(double deltaSeconds) =>
        new(ProductUpdateMode.Realtime, ProductLifecycleState.Running, 1, 1, 1, 1, 60, 1, 0, deltaSeconds);
}
