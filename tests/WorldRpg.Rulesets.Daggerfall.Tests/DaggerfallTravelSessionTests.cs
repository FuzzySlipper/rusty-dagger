using System.Text.Json;
using System.Reflection;
using System.Text.Json.Nodes;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Travel;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallTravelSessionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Sunlight_career_arrives_at_dusk_at_both_speeds_and_no_regen_preserves_magicka(bool cautious, bool restoreFirst)
    {
        using Fixture fixture = new(); fixture.AddGold(1000);
        CareerPassiveSessionTests.SetCareer(fixture.Session, [new("regenerate-health", "general")],
            [new("damage", "sunlight"), new("inability-to-regen")]);
        var baseline = DaggerfallSavePayload.Read(fixture.Session.CaptureSave());
        // Begin at dawn so the short source-backed route reaches its sunlight arrival adjustment.
        baseline = baseline with { Calendar = baseline.Calendar with { Hour = 7, Minute = 0, Second = 0 } };
        using var started = fixture.Restore(DaggerfallSavePayload.Encode(baseline));
        using var restored = restoreFirst ? fixture.Restore(started.CaptureSave()) : null;
        var session = restored ?? started;
        var magicka = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("magicka")); magicka.SetCurrent(1);
        session.Update(new ProductUpdate(OuterUpdate(1), [Ui(JsonSerializer.Serialize(new { action = "travel-preview",
            region = fixture.Destination.Site!.Value.Region, destination = fixture.Destination.Site.Value.Index,
            cautious, inn = true, ship = false }))]));
        var quote = session.ReadTravelPresentation().Quote!;
        Assert.NotNull(quote);
        session.Update(new ProductUpdate(OuterUpdate(2), [Ui(JsonSerializer.Serialize(new { action = "travel-accept", key = quote.Identity, amount = quote.TotalCost }))]));
        Assert.Equal(DaggerfallTravelOutcome.Arrived, session.State.Travel.LastResult!.Outcome);
        var arrived = DaggerfallSavePayload.Read(session.CaptureSave()).Calendar;
        Assert.False(new DaggerfallCalendar(arrived.Year, arrived.Month, arrived.Day, arrived.Hour, arrived.Minute, arrived.Second).IsDay);
        Assert.Equal(1, magicka.Current);
        Assert.False(session.State.Travel.IsExecuting);
    }

    [Fact]
    public void Accepted_ui_journey_pays_once_reaches_real_anchor_and_restores_result()
    {
        using Fixture fixture = new();
        fixture.AddGold(1000);
        DaggerfallTravelQuote quote = fixture.Preview(inn: true);
        Assert.True(quote.InnCost > 0);
        ulong before = fixture.Session.State.Currency.Read().Gold;
        fixture.Accept(quote, duplicate: true);
        var result = Assert.IsType<DaggerfallTravelResult>(fixture.Session.State.Travel.LastResult);
        Assert.Equal(DaggerfallTravelOutcome.Arrived, result.Outcome);
        Assert.Equal(quote.TotalCost, result.PaidGold);
        Assert.Equal(before - (ulong)quote.TotalCost, fixture.Session.State.Currency.Read().Gold);
        Assert.Equal(fixture.Destination.Site!.Value, fixture.Session.Site.Active);
        WorldPoint destination = fixture.Destination.Project.PlayerPosition
            ?? throw new InvalidOperationException("The travel destination has no authored player position.");
        WorldPoint liveDestination = fixture.Session.State.PlayerControl.Position
            ?? throw new InvalidOperationException("The travel session has no live arrival position.");
        // A travel destination is authored in its site's local frame. The active exterior keeps
        // the map-pixel translation in the current local origin, so this owner converts the live
        // pose back to that frame instead of comparing it with a raw global local pose.
        Assert.Equal(destination, fixture.Session.Sites.ExteriorSitePosition(liveDestination));
        WorldPoint savedProfileDestination = WorldPoint.From(fixture.Session.Sites.LocalToProfile(
            liveDestination.ToVector()));
        Assert.True(result.ElapsedSeconds >= quote.TravelSeconds);
        RulesetSavePayload save = fixture.Session.CaptureSave();
        DaggerfallSavePayload captured = DaggerfallSavePayload.Read(save);
        WorldPoint savedDestination = new(captured.Player.X, captured.Player.Y, captured.Player.Z);
        Assert.Equal(liveDestination, savedDestination);
        using var restored = fixture.Restore(save);
        Assert.Equal(result, restored.State.Travel.LastResult);
        Assert.Equal(fixture.Session.State.Currency.Read(), restored.State.Currency.Read());
        Assert.Equal(fixture.Session.Site.Active, restored.Site.Active);
        WorldPoint restoredDestination = restored.State.PlayerControl.Position
            ?? throw new InvalidOperationException("The restored travel session has no arrival position.");
        Assert.Equal(savedDestination, restoredDestination);
        Assert.Equal(destination, restored.Sites.ExteriorSitePosition(restoredDestination));
        Assert.Equal(savedProfileDestination, WorldPoint.From(restored.Sites.LocalToProfile(
            restoredDestination.ToVector())));
        Assert.Null(restored.ReadTravelPresentation().Quote);
        Assert.Contains("Arrived", restored.ReadTravelPresentation().Message);
        // Elapsed time reads as game time, not raw seconds.
        Assert.Contains(" passed.", result.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("seconds", result.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unquotable_destination_reports_a_player_sentence_instead_of_route_detail()
    {
        using Fixture fixture = new();
        fixture.PreviewSite(fixture.Origin.Site!.Value);
        DaggerfallTravelPresentation travel = fixture.Session.ReadTravelPresentation();
        Assert.Null(travel.Quote);
        Assert.Equal("No travel route to that destination is available right now.", travel.Message);
    }

    [Fact]
    public void Destinations_are_labelled_with_their_kind_and_published_region_name()
    {
        using Fixture fixture = new();
        DaggerfallTravelDestination destination = fixture.Preview(inn: false).Destination;
        DaggerfallTravelPresentation travel = fixture.Session.ReadTravelPresentation();
        Assert.Equal("City", DaggerfallSiteKinds.Label(destination.Kind));
        Assert.Equal(TestPayload.Definitions.BuildingNames.RegionNames[destination.Id.Region], travel.RegionName(destination.Id.Region));
        Assert.Equal("an unnamed region", travel.RegionName(-1));
    }

    [Fact]
    public void A_journey_rerolls_a_held_item_once_on_arrival_like_one_synthetic_time_increase()
    {
        using Fixture fixture = new(distant: true);
        fixture.AddGold(100_000);
        DaggerfallSession session = fixture.Session;
        var created = new DaggerfallItemFactory(TestPayload.Definitions, RandomMinimum.Create())
            .Create(new("Magic", "sanguine-rose", DaggerfallItemOwner.Player, MagicItemKey: "magic-item.0035", Race: "breton", Gender: "male"));
        var identity = session.UniqueItemAllocator.AllocateReference();
        var item = session.State.Equipment.Materialize(identity, created.Item);
        session.State.ItemInstances.RegisterUnique(identity.Value, created.Metadata);
        ItemCastTriggerSessionTests.Equip(session, item);
        long sequence = session.Casting.NextSequence;

        fixture.Accept(fixture.Preview(inn: true));

        var result = Assert.IsType<DaggerfallTravelResult>(session.State.Travel.LastResult);
        Assert.Equal(DaggerfallTravelOutcome.Arrived, result.Outcome);
        Assert.True(result.EndedSeconds - result.StartedSeconds > DaggerfallCalendar.SecondsPerDay);
        var held = session.State.ItemInstances.RequireUnique(identity.Value).HeldCast!;
        Assert.Equal(sequence + 1, session.Casting.NextSequence);
        Assert.False(held.RerollPending);
        Assert.Equal(result.EndedSeconds / DaggerfallCalendar.SecondsPerMinute, held.LastRerollMinute);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Only_cautious_travel_restores_vitals_through_attached_tracks(bool cautious)
    {
        using Fixture fixture = new();
        fixture.AddGold(1000);
        var quote = fixture.Preview(inn: true, cautious: cautious);
        var stats = fixture.Session.State.Actors.Player.Stats;
        foreach (string name in new[] { "health", "stamina", "magicka" }) stats.GetTrack(TrackId.Parse(name)).SetCurrent(1);
        fixture.Accept(quote);
        var result = Assert.IsType<DaggerfallTravelResult>(fixture.Session.State.Travel.LastResult);
        Assert.Equal(DaggerfallTravelOutcome.Arrived, result.Outcome);
        Assert.Equal(cautious ? stats.GetTrack(TrackId.Parse("health")).Maximum.Value : 1,
            stats.GetTrack(TrackId.Parse("health")).Current);
        Assert.Equal(cautious ? stats.GetTrack(TrackId.Parse("magicka")).Maximum.Value : 1,
            stats.GetTrack(TrackId.Parse("magicka")).Current);

        // Cautious recovery happens before travel. Elapsed travel and its arrival delay then
        // settle every covered calendar minute through the locomotion owner's idle fatigue rule.
        Track stamina = stats.GetTrack(TrackId.Parse("stamina"));
        long coveredMinutes = result.EndedSeconds / DaggerfallCalendar.SecondsPerMinute
            - result.StartedSeconds / DaggerfallCalendar.SecondsPerMinute;
        Assert.True(coveredMinutes > 0);
        double startingStamina = cautious ? stamina.Maximum.Value : 1;
        double expectedStamina = Math.Max(0, startingStamina
            - (coveredMinutes * (long)DaggerfallTuning.Defaults.Locomotion.IdleFatiguePerGameMinute));
        Assert.Equal(expectedStamina, stamina.Current);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Actual_ship_fare_accepts_letters_and_reserves_inn_coins(bool inn)
    {
        using Fixture fixture = new(ocean: true);
        fixture.AddLetter(2000);
        DaggerfallTravelQuote initial = fixture.Preview(inn: inn, ship: true);
        Assert.True(initial.ShipCost > 0);
        if (inn)
        {
            Assert.False(initial.CanAfford);
            fixture.AddGold((ulong)initial.InnCost);
        }
        DaggerfallTravelQuote quote = fixture.Preview(inn: inn, ship: true);
        var before = fixture.Session.State.Currency.Read();
        fixture.Accept(quote);
        var after = fixture.Session.State.Currency.Read();
        Assert.Equal(DaggerfallTravelOutcome.Arrived, fixture.Session.State.Travel.LastResult!.Outcome);
        Assert.Equal(before.Gold - (ulong)quote.InnCost, after.Gold);
        Assert.Equal(before.LettersOfCredit - (ulong)quote.ShipCost, after.LettersOfCredit);
        Assert.Equal(before.AccountGold, after.AccountGold);
    }

    [Fact]
    public void Insufficient_inn_coins_and_stale_funds_quote_leave_payment_time_and_location_unchanged()
    {
        using Fixture fixture = new();
        fixture.AddLetter(1000);
        DaggerfallTravelQuote poor = fixture.Preview(inn: true);
        Assert.False(poor.CanAfford);
        var before = fixture.Session.State.Currency.Read();
        long now = fixture.Now;
        fixture.Accept(poor);
        Assert.Null(fixture.Session.State.Travel.LastResult);
        Assert.Equal(before, fixture.Session.State.Currency.Read());
        Assert.Equal(now, fixture.Now);
        fixture.AddGold(100);
        DaggerfallTravelQuote stale = fixture.Preview(inn: true);
        fixture.AddGold(1);
        before = fixture.Session.State.Currency.Read();
        now = fixture.Now;
        fixture.Accept(stale);
        Assert.Null(fixture.Session.State.Travel.LastResult);
        Assert.Equal(before, fixture.Session.State.Currency.Read());
        Assert.Equal(now, fixture.Now);
        Assert.Equal(fixture.Origin.Site!.Value, fixture.Session.Site.Active);
        Assert.Contains("changed", fixture.Session.ReadTravelPresentation().Message);
    }

    [Fact]
    public void Missing_arrival_profile_is_explained_before_payment()
    {
        using Fixture fixture = new(admitDestination: false);
        fixture.AddGold(100);
        DaggerfallTravelQuote quote = fixture.Preview(inn: true);
        long now = fixture.Now;
        fixture.Accept(quote);
        Assert.Null(fixture.Session.State.Travel.LastResult);
        Assert.Equal(100UL, fixture.Session.State.Currency.Read().Gold);
        Assert.Equal(now, fixture.Now);
        Assert.Contains("arrival profile", fixture.Session.ReadTravelPresentation().Message);
        Assert.False(fixture.Session.ReadTravelPresentation().ExecutionAvailable);
    }

    [Fact]
    public void Defeating_effect_interrupts_paid_journey_at_actual_minute_and_restore_never_recharges()
    {
        using Fixture fixture = new(round: effect => effect.Target.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).SetCurrent(0));
        fixture.AddGold(1000);
        DaggerfallTravelQuote quote = fixture.Preview(inn: true);
        fixture.StartEffect();
        fixture.Accept(quote);
        var result = fixture.Session.State.Travel.LastResult!;
        Assert.Equal(DaggerfallTravelOutcome.Defeated, result.Outcome);
        Assert.InRange(result.ElapsedSeconds, 1, 60);
        Assert.True(result.ElapsedSeconds < result.QuotedSeconds);
        Assert.Equal(fixture.Origin.Site!.Value, result.ActualSite);
        Assert.Equal(1000UL - (ulong)quote.TotalCost, fixture.Session.State.Currency.Read().Gold);
        using var restored = fixture.Restore(fixture.Session.CaptureSave());
        Assert.Equal(result, restored.State.Travel.LastResult);
        Assert.Equal(fixture.Session.State.Currency.Read(), restored.State.Currency.Read());
        Assert.Equal(fixture.Now, Absolute(restored.CaptureSave()));
        Assert.Equal(fixture.Origin.Site!.Value, restored.Site.Active);
    }

    [Fact]
    public void Camp_encounter_interrupts_at_saved_origin_and_its_selection_survives_restore()
    {
        using Fixture fixture = new(encounter: true);
        DaggerfallTravelQuote quote = fixture.Preview(inn: false);
        Assert.Equal(0, quote.TotalCost);
        fixture.Accept(quote);
        var result = fixture.Session.State.Travel.LastResult!;
        Assert.Equal(DaggerfallTravelOutcome.Encounter, result.Outcome);
        Assert.InRange(result.ElapsedSeconds, 1, 60);
        Assert.True(result.ElapsedSeconds < quote.TravelSeconds);
        Assert.Equal(fixture.Origin.Site!.Value, result.ActualSite);
        var saved = fixture.Session.CaptureSave();
        var encounter = Assert.Single(DaggerfallSavePayload.Read(saved).Encounters.Resolved);
        Assert.NotNull(encounter.Choice.MobileId);
        Assert.Null(encounter.SpawnedActorId);
        using var restored = fixture.Restore(saved);
        Assert.Equal(result, restored.State.Travel.LastResult);
        Assert.Equal(encounter, Assert.Single(DaggerfallSavePayload.Read(restored.CaptureSave()).Encounters.Resolved));
        Assert.Equal(fixture.Now, Absolute(restored.CaptureSave()));
        Assert.Equal(0UL, restored.State.Currency.Read().Gold);
    }

    [Fact]
    public void Arrival_adjustment_effect_observes_destination_and_interruption_keeps_arrived_location()
    {
        Fixture? current = null;
        using Fixture fixture = current = new(round: effect =>
        {
            if (current!.Session.Site.Active == current.Destination.Site)
                effect.Target.Get<StatsComponent>().GetTrack(TrackId.Parse("health")).SetCurrent(0);
        });
        fixture.AddGold(1000);
        var quote = fixture.Preview(inn: true, cautious: true);
        long predicted = (fixture.Now + quote.TravelSeconds) % DaggerfallCalendar.SecondsPerDay;
        fixture.Session.AdvanceElapsedTime((18 * 3600 - predicted + DaggerfallCalendar.SecondsPerDay) % DaggerfallCalendar.SecondsPerDay);
        fixture.StartEffect(10000);
        fixture.Accept(quote);
        var result = fixture.Session.State.Travel.LastResult!;
        Assert.Equal(DaggerfallTravelOutcome.Defeated, result.Outcome);
        Assert.Equal(fixture.Destination.Site!.Value, result.ActualSite);
        Assert.Equal(quote.Destination.MapPixel, result.ActualPixel);
        Assert.InRange(result.ElapsedSeconds - quote.TravelSeconds, 1, 60);
        using var restored = fixture.Restore(fixture.Session.CaptureSave());
        Assert.Equal(fixture.Destination.Site, restored.Site.Active);
        Assert.Equal(result, restored.State.Travel.LastResult);
    }

    [Fact]
    public void Capture_during_effect_round_terminates_saved_attempt_with_paid_amount_and_actual_time()
    {
        RulesetSavePayload? captured = null;
        Fixture? current = null;
        using Fixture fixture = current = new(round: _ => captured ??= current!.Session.CaptureSave());
        fixture.AddGold(1000);
        DaggerfallTravelQuote quote = fixture.Preview(inn: true);
        fixture.StartEffect();
        fixture.Accept(quote);
        Assert.Equal(DaggerfallTravelOutcome.Arrived, fixture.Session.State.Travel.LastResult!.Outcome);
        Assert.NotNull(captured);
        using var restored = fixture.Restore(captured);
        var savedResult = Assert.IsType<DaggerfallTravelResult>(restored.State.Travel.LastResult);
        Assert.Equal(DaggerfallTravelOutcome.SaveBoundary, savedResult.Outcome);
        Assert.Equal(quote.TotalCost, savedResult.PaidGold);
        Assert.InRange(savedResult.ElapsedSeconds, 1, 60);
        Assert.Equal(1000UL - (ulong)quote.TotalCost, restored.State.Currency.Read().Gold);
        Assert.Equal(savedResult.EndedSeconds, Absolute(restored.CaptureSave()));
        Assert.Equal(fixture.Origin.Site!.Value, restored.Site.Active);
        Assert.Null(restored.ReadTravelPresentation().Quote);
        Assert.Contains("stopped", restored.ReadTravelPresentation().Message);
    }

    [Fact]
    public void Multi_day_journey_expires_live_quest_clock_before_arrival_and_retains_terminal_journal()
    {
        using Fixture fixture = new(definitions: DeadlineDefinitions.Value, distant: true);
        fixture.AddGold(1000);
        fixture.Session.State.Quests.Start(new("travel-deadline", "travel-deadline.txt", "travel-deadline",
            DaggerfallQuestLifecycle.Active, null, [], []));
        fixture.Session.State.Quests.Advance(fixture.Session.State.Variables, DaggerfallCalendar.Start);
        Assert.True(Assert.Single(Assert.Single(fixture.Session.State.Quests.All).Clocks).Enabled);
        DaggerfallTravelQuote quote = fixture.Preview(inn: true);
        Assert.True(quote.TravelSeconds > DaggerfallCalendar.SecondsPerDay);
        fixture.Accept(quote);
        Assert.Equal(DaggerfallTravelOutcome.Arrived, fixture.Session.State.Travel.LastResult!.Outcome);
        Assert.DoesNotContain(fixture.Session.State.Quests.All, quest => quest.Lifecycle == DaggerfallQuestLifecycle.Active);
        var journal = fixture.Session.State.Quests.ReadPresentation(_ => throw new Exception("Ended quest must retain its own text.")).Journal;
        Assert.Contains(journal, entry => entry.Text == "The deadline expired during travel.");
        using var restored = fixture.Restore(fixture.Session.CaptureSave());
        Assert.DoesNotContain(restored.State.Quests.All, quest => quest.Lifecycle == DaggerfallQuestLifecycle.Active);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Sunlight_arrival_policy_applies_to_both_travel_speeds(bool cautious) =>
        Assert.Equal(6 * 3600, DaggerfallSession.TravelArrivalDelay(new(405, 5, 0, 12, 0, 0), true, cautious));

    [Theory]
    [InlineData(7, 9, false, 60)]
    [InlineData(7, 10, false, 0)]
    [InlineData(17, 59, false, 0)]
    [InlineData(18, 0, false, 47400)]
    [InlineData(12, 0, true, 21600)]
    [InlineData(19, 0, true, 0)]
    public void Cautious_arrival_retains_donor_morning_and_sunlight_boundaries(int hour, int minute, bool sunlight, long delay) =>
        Assert.Equal(delay, DaggerfallSession.TravelArrivalDelay(new(405, 1, 1, hour, minute, 0), sunlight));

    [Fact]
    public void Current_travel_save_requires_result_fields_and_refuses_false_arrival()
    {
        using Fixture fixture = new();
        fixture.AddGold(100);
        fixture.Accept(fixture.Preview(inn: true));
        DaggerfallSavePayload saved = DaggerfallSavePayload.Read(fixture.Session.CaptureSave());
        var result = saved.Travel.LastResult!;
        Assert.Throws<ArgumentException>(() => (saved with { Travel = new(result with { ActualSite = result.Origin }) }).Validate());
        Assert.Throws<ArgumentException>(() => (saved with { Travel = new(result with { EndedSeconds = result.StartedSeconds }) }).Validate());
        JsonObject malformed = JsonNode.Parse(fixture.Session.CaptureSave().Bytes.Span)!.AsObject();
        malformed["Travel"]!["LastResult"]!.AsObject().Remove("PaidGold");
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize(malformed.ToJsonString(), DaggerfallSaveJsonContext.Default.DaggerfallSavePayload));
    }

    private static long Absolute(RulesetSavePayload save)
    {
        var date = DaggerfallSavePayload.Read(save).Calendar;
        return new DaggerfallCalendar(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second).ToAbsoluteSeconds();
    }
    private static readonly Lazy<DaggerfallDefinitions> DeadlineDefinitions = new(() =>
    {
        JsonObject root = JsonNode.Parse(TestPayload.CombinedText)!.AsObject();
        root["questSources"]!["quests"]!.AsArray().Add(JsonNode.Parse("""
            {"name":"travel-deadline","displayName":"Travel deadline","sourceFile":"travel-deadline.txt","disposition":"compiled",
            "messages":[{"id":10,"firstLine":1,"lines":["The deadline expired during travel."]}],
            "blocks":[{"kind":"clock","firstLine":2,"lines":["clock _deadline_ 1"],"global":null},
            {"kind":"task","firstLine":3,"lines":["_deadline_ task:","log 10 step 1","end quest"],"global":null},
            {"kind":"headless","firstLine":6,"lines":["start timer _deadline_"],"global":null}],"diagnostics":[]}
            """));
        return DaggerfallBaseContent.Read(System.Text.Encoding.UTF8.GetBytes(root.ToJsonString()));
    });
    private static readonly Lazy<(DaggerfallSiteProfile Profile, ResolvedCompositionIdentity Identity)> Inputs = new(() =>
        (ReadInputs(TestData.RepositoryRoot), GameCompositionResolver.Resolve(FullContent(TestData.RepositoryRoot),
            new GameBundleId("daggerfall.privateers-hold")).RequireComposition().Identity));

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallDefinitions definitions;
        private readonly List<string> releases = [];
        private readonly DaggerfallSiteProfiles profiles;
        private readonly DaggerfallEffectCatalog? effects;
        private readonly bool encounter;
        private ulong step = 1;
        internal DaggerfallSession Session { get; }
        internal DaggerfallSiteProfile Origin { get; }
        internal DaggerfallSiteProfile Destination { get; }
        internal long Now => Absolute(Session.CaptureSave());
        internal Fixture(bool ocean = false, bool distant = false, bool admitDestination = true, bool encounter = false,
            Action<DaggerfallActiveEffect>? round = null, DaggerfallDefinitions? definitions = null)
        {
            this.definitions = definitions ?? TestPayload.Definitions; this.encounter = encounter;
            var source = Inputs.Value.Profile;
            var sites = this.definitions.Locations.Records.Where(site => site.Exterior is not null && site.Kind == DaggerfallSiteKind.TownCity
                && this.definitions.Grids.Climate.GetCell(site.MapPixelX, site.MapPixelY).Value is >= 224 and <= 232).ToArray();
            var origin = sites[0];
            var context = new DaggerfallSiteContext(this.definitions.Locations);
            var policy = new DaggerfallTravelPolicy(context, this.definitions.Grids, DaggerfallTuning.Defaults.Transport);
            var options = new DaggerfallTravelOptions(false, true, ocean, false, false, false);
            var candidates = sites.Where(site => site.MapPixelX != origin.MapPixelX || site.MapPixelY != origin.MapPixelY)
                .OrderBy(site => Math.Abs(site.MapPixelX - origin.MapPixelX) + Math.Abs(site.MapPixelY - origin.MapPixelY));
            var destination = candidates.First(site =>
            {
                var quote = policy.Quote(new(origin.MapPixelX, origin.MapPixelY), site.Id, options, requireDiscovered: false);
                return (!ocean || quote.ShipCost > 0) && (!distant || quote.TravelSeconds > DaggerfallCalendar.SecondsPerDay);
            });
            Origin = Profile(origin.Id, "travel-origin"); Destination = Profile(destination.Id, "travel-arrival");
            profiles = new(admitDestination ? [Origin, Destination] : [Origin]);
            int rounds = 0;
            effects = round is null ? null : new([new("travel-test", "travel-test", DaggerfallEffectStacking.Stack, 1, 1,
                MagicRound: effect => { if (++rounds > 1) round(effect); })]);
            Session = Create(null); Session.Site.Discover(destination.Id);
            if (Session.State.Currency.Read().Gold > 0) Assert.True(Session.State.Currency.TrySpendCarried(Session.State.Currency.Read().Gold));
            DaggerfallSiteProfile Profile(DaggerfallSiteId id, string name) => new(
                new ProjectFacts(new WorldPoint(name == "travel-origin" ? 1 : 3, 1, 1), new Dictionary<long, AuthoredActor>()), source.SpatialArtifact,
                source.StaticMesh, source.WorldAppearance, source.InitialLook, source.Materials, new Dictionary<long, NormalizedActorSprite>(),
                source.MobileSprites, source.Audio, source.ClassicPresentation, id,
                profileKind: DaggerfallWorldProfileKind.Exterior, logicalProfileId: name,
                terrainTextures: source.TerrainTextures, billboardSprites: source.BillboardSprites);
        }
        private DaggerfallSession Create(RulesetSavePayload? save)
        {
            ContentFake content = new(releases);
            PopulateContent(content, Origin); PopulateContent(content, Destination);
            PopulateTerrainContent(content, Origin); PopulateTerrainContent(content, Destination);
            var engine = EngineContextFake.Create(content, SpatialFake.Create(Origin.SpatialArtifact.Sha256, releases).Service,
                new AppearanceFake(releases), random: encounter ? TravelEncounterRandom.CreateEncounter() : LodgingRandom.Create());
            DaggerfallSkyMedia sky = DaggerfallSkyMedia.Read(FullContent(TestData.RepositoryRoot));
            DaggerfallSessionComposition composition = new(definitions, Origin, DaggerfallTuning.Defaults, Inputs.Value.Identity)
                { Profiles = profiles, Effects = effects, Sky = sky };
            return save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
        }
        internal DaggerfallSession Restore(RulesetSavePayload save) => Create(save);
        internal DaggerfallTravelQuote Preview(bool inn, bool ship = false, bool cautious = false)
        {
            Submit(new { action = "travel-preview", region = Destination.Site!.Value.Region, destination = Destination.Site!.Value.Index,
                cautious, inn, ship });
            return Session.ReadTravelPresentation().Quote!;
        }
        internal void PreviewSite(DaggerfallSiteId site) => Submit(new { action = "travel-preview", region = site.Region,
            destination = site.Index, cautious = false, inn = false, ship = false });
        private void Submit(object action) => Session.Update(new ProductUpdate(OuterUpdate(step++), [Ui(JsonSerializer.Serialize(action))]));
        internal void Accept(DaggerfallTravelQuote quote, bool duplicate = false)
        {
            string action = JsonSerializer.Serialize(new { action = "travel-accept", key = quote.Identity, amount = quote.TotalCost });
            Session.Update(new ProductUpdate(OuterUpdate(step++), duplicate ? [Ui(action), Ui(action)] : [Ui(action)]));
        }
        internal void AddGold(ulong quantity)
        {
            var item = definitions.RequireItem(new DaggerfallItemId("gold-piece"));
            var stack = InventoryStackId.Parse("travel.gold");
            Session.State.Inventory.Grant(new(new InventoryItemId(item.Id.Value), stack, quantity));
            if (!Session.State.ItemInstances.ContainsStack(DaggerfallItemOwner.Player, stack))
                Session.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Player, stack, DaggerfallItemInstanceMetadata.Default(item, DaggerfallItemOwner.Player));
        }
        internal void AddLetter(ulong amount)
        {
            Assert.True(Session.State.Currency.TryCreditAccount(amount + amount / 100));
            Assert.True(Session.State.Currency.WithdrawLetter(amount));
        }
        internal void StartEffect(uint rounds = 4) => Session.State.Effects.Start(new("travel-test-instance", "travel-test", "spell", null,
            DaggerfallActorIdentity.PlayerEntityId, "classic", "magic", null, 1, rounds, JsonDocument.Parse("{}").RootElement.Clone()));
        public void Dispose() => Session.Dispose();

        private static void PopulateTerrainContent(ContentFake content, DaggerfallSiteProfile profile)
        {
            foreach (NormalizedTerrainTexture texture in profile.TerrainTextures.Values)
                content.Add(texture.TexturePath, texture.TextureSha256);
        }
    }
}

internal class TravelEncounterRandom : LodgingRandom
{
    internal static IRandomService CreateEncounter() => DispatchProxy.Create<IRandomService, TravelEncounterRandom>();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name == nameof(IRandomService.DrawKeyed)
        ? new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Minimum) : base.Invoke(method, arguments);
}
