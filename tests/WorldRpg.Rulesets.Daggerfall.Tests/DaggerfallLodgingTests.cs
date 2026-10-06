using System.Text.Json;
using System.Reflection;
using WorldRpg.Rulesets.Daggerfall.Guilds;
using Rusty.Engine;
using Rusty.Engine.Mechanics;
using Rusty.Engine.Entities;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallLodgingTests
{
    [Theory]
    [InlineData(1, 45, 7)]
    [InlineData(2, 45, 7)]
    [InlineData(1, 46, 0)]
    [InlineData(2, 46, 7)]
    [InlineData(1, 47, 7)]
    [InlineData(350, 1, 2443)]
    [InlineData(350, 360, 2450)]
    public void Room_cost_preserves_source_holiday_boundary_without_wrapping(int days, int day, int expected) =>
        Assert.Equal(expected, DaggerfallLodgingState.CalculateRoomCost(days, day));

    [Fact]
    public void Quote_reuses_trade_rounding_and_free_room_privilege()
    {
        Assert.Equal(3UL, DaggerfallLodgingState.Quote(1, DaggerfallCalendar.Start, 10, 50, 50, false));
        Assert.Equal(0UL, DaggerfallLodgingState.Quote(1, new(405, 1, 15, 0, 0, 0), 10, 50, 50, false));
        Assert.Equal(0UL, DaggerfallLodgingState.Quote(350, DaggerfallCalendar.Start, 20, 0, 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => DaggerfallLodgingState.CalculateRoomCost(351, 1));
    }

    [Fact]
    public void Paid_booking_extends_rest_recovers_and_restores_exact_room()
    {
        using Fixture fixture = new();
        var session = fixture.Session;
        fixture.Rest(1);
        Assert.Equal(0, session.RestView.ElapsedSeconds);
        Assert.Contains("room", session.RestView.Message);
        fixture.AddGold(1000);
        ulong gold = session.State.Currency.Read().Gold;
        DaggerfallLodgingView quote = session.LodgingView!;
        Assert.True(quote.Price > 0);
        fixture.Book(quote);
        Assert.Equal(gold - quote.Price, session.State.Currency.Read().Gold);
        Assert.Equal(24, session.LodgingView!.RemainingHours);
        Track health = session.State.Actors.Player.Stats.GetTrack(TrackId.Parse("health"));
        health.SetCurrent(health.MaximumValue - 10);
        fixture.Rest(1);
        Assert.Equal(3600, session.RestView.ElapsedSeconds);
        Assert.True(session.RestView.HealthRecovered > 0);
        Assert.Equal(23, session.LodgingView!.RemainingHours);
        fixture.Quote(2);
        fixture.Book(session.LodgingView!);
        Assert.Equal(71, session.LodgingView!.RemainingHours);
        var saved = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Single(saved.Lodging.Rooms);
        using DaggerfallSession restored = fixture.Restore(DaggerfallSavePayload.Encode(saved));
        Assert.Equal(71, restored.LodgingView!.RemainingHours);
        restored.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));
        Assert.Equal(3600, restored.RestView.ElapsedSeconds);
        Assert.Equal(70, restored.LodgingView!.RemainingHours);
    }

    [Fact]
    public void Unfunded_or_stale_booking_does_not_change_gold_or_room()
    {
        using Fixture fixture = new();
        DaggerfallSession session = fixture.Session;
        fixture.Quote(350);
        fixture.Book(session.LodgingView!);
        Assert.Equal(0, session.LodgingView!.RemainingHours);
        Assert.Contains("enough gold", session.Presentation.LastOutcome);
        fixture.AddGold(1000);
        ulong gold = session.State.Currency.Read().Gold;
        DaggerfallLodgingView quote = session.LodgingView!;
        fixture.Submit(new { action = "lodging-book", key = quote.Key + "0", days = 1, amount = quote.Price });
        fixture.Submit(new { action = "lodging-book", key = quote.Key, days = 1, amount = 999 });
        Assert.Equal(gold, session.State.Currency.Read().Gold);
        Assert.Empty(DaggerfallSavePayload.Read(session.CaptureSave()).Lodging.Rooms);
    }

    [Fact]
    public void Booking_survives_exit_and_other_tavern_does_not_share_the_privilege()
    {
        using Fixture fixture = new();
        var session = fixture.Session;
        fixture.AddGold(1000);
        var quote = session.LodgingView!;
        fixture.Book(quote);
        Assert.True(session.TryRelocate(new(fixture.Exterior.ProfileKey, "start")));
        Assert.Null(session.LodgingView);
        fixture.Submit(new { action = "lodging-book", key = quote.Key, days = 1, amount = quote.Price });
        Assert.Contains("no longer", session.Presentation.LastOutcome);
        Assert.True(session.TryRelocate(new(fixture.Other.ProfileKey, "start")));
        Assert.Equal(0, session.LodgingView!.RemainingHours);
        fixture.Rest(1);
        Assert.Equal(0, session.RestView.ElapsedSeconds);
        Assert.True(session.TryRelocate(new(fixture.Interior.ProfileKey, "start")));
        Assert.Equal(24, session.LodgingView!.RemainingHours);
        fixture.Rest(1);
        Assert.Equal(3600, session.RestView.ElapsedSeconds);
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(23, restored.LodgingView!.RemainingHours);
    }

    [Fact]
    public void Rental_expires_during_rest_and_no_recovery_or_booking_survives_past_expiry()
    {
        using Fixture fixture = new();
        fixture.AddGold(1000);
        fixture.Book(fixture.Session.LodgingView!);
        fixture.Rest(25);
        Assert.Equal(24 * 3600, fixture.Session.RestView.ElapsedSeconds);
        Assert.Equal(DaggerfallRestInterruption.Prevented, fixture.Session.RestView.Interruption);
        Assert.Equal(0, fixture.Session.LodgingView!.RemainingHours);
        fixture.Rest(1);
        Assert.Equal(0, fixture.Session.RestView.ElapsedSeconds);
        var saved = DaggerfallSavePayload.Read(fixture.Session.CaptureSave());
        Assert.Empty(saved.Lodging.Rooms);
        using DaggerfallSession restored = fixture.Restore(DaggerfallSavePayload.Encode(saved));
        restored.Update(new ProductUpdate(OuterUpdate(1), [Ui("{\"action\":\"rest\",\"mode\":\"timed\",\"hours\":1}")]));
        Assert.Equal(0, restored.RestView.ElapsedSeconds);
    }

    [Fact]
    public void Mid_minute_expiry_caps_the_admitted_rest_slice_at_the_exact_paid_second()
    {
        using Fixture fixture = new();
        fixture.Session.AdvanceElapsedTime(30);
        fixture.Book(fixture.Session.LodgingView!);
        fixture.Session.AdvanceElapsedTime(DaggerfallCalendar.SecondsPerDay - 15);
        Assert.Equal(1, fixture.Session.LodgingView!.RemainingHours);
        fixture.Rest(1);
        Assert.Equal(15, fixture.Session.RestView.ElapsedSeconds);
        Assert.Equal(0, fixture.Session.RestView.RecoveryHours);
        Assert.Equal(DaggerfallRestInterruption.Prevented, fixture.Session.RestView.Interruption);
        Assert.Equal(0, fixture.Session.LodgingView!.RemainingHours);
    }

    [Fact]
    public void Extension_limit_and_malformed_current_booking_fail_before_payment_or_load()
    {
        using Fixture fixture = new();
        fixture.AddGold(10000);
        fixture.Quote(350);
        fixture.Book(fixture.Session.LodgingView!);
        ulong gold = fixture.Session.State.Currency.Read().Gold;
        fixture.Quote(1);
        Assert.False(fixture.Session.LodgingView!.CanBook);
        fixture.Book(fixture.Session.LodgingView!);
        Assert.Equal(gold, fixture.Session.State.Currency.Read().Gold);
        var saved = DaggerfallSavePayload.Read(fixture.Session.CaptureSave());
        var room = Assert.Single(saved.Lodging.Rooms);
        Assert.Throws<ArgumentException>(() => fixture.Restore(DaggerfallSavePayload.Encode(saved with {
            Lodging = new([room with { BuildingIndex = 99999 }]) })));
        Assert.Throws<ArgumentException>(() => DaggerfallSavePayload.Encode(saved with { Lodging = new([room, room]) }));
    }

    [Fact]
    public void Knightly_free_room_privilege_reaches_the_real_booking_and_restore()
    {
        using Fixture fixture = new();
        var session = fixture.Session;
        var guild = DaggerfallConcreteGuildCatalog.All.First(value => value.TryGetService(DaggerfallConcreteGuildService.FreeTavernRooms, out _));
        int day = checked((int)DaggerfallCalendar.Start.DayNumber);
        session.State.Social.JoinGuild(guild.FactionId, day);
        for (int rank = 0; rank < 4; rank++) session.State.Social.PromoteGuild(guild.FactionId, day);
        Assert.True(session.State.Currency.TrySpendGold(session.State.Currency.Read().Gold, []));
        Assert.Equal(0UL, session.State.Currency.Read().Gold);
        Assert.Equal(0UL, session.LodgingView!.Price);
        fixture.Book(session.LodgingView!);
        Assert.Equal(24, session.LodgingView!.RemainingHours);
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(24, restored.LodgingView!.RemainingHours);
        Assert.Equal(0UL, restored.LodgingView!.Price);
    }

    [Fact]
    public void Booking_spends_real_letters_of_credit_when_carried_coins_are_insufficient()
    {
        using Fixture fixture = new();
        var session = fixture.Session;
        Assert.True(session.State.Currency.TrySpendGold(session.State.Currency.Read().Gold, []));
        Assert.True(session.State.Currency.TryCreditAccount(101));
        Assert.True(session.State.Currency.WithdrawLetter(100));
        var quote = session.LodgingView!;
        Assert.True(quote.Price > 0);
        fixture.Book(quote);
        Assert.Equal(0UL, session.State.Currency.Read().Gold);
        Assert.Equal(100UL - quote.Price, session.State.Currency.Read().LettersOfCredit);
        Assert.Equal(24, session.LodgingView!.RemainingHours);
        using DaggerfallSession restored = fixture.Restore(session.CaptureSave());
        Assert.Equal(100UL - quote.Price, restored.State.Currency.Read().LettersOfCredit);
        Assert.Equal(24, restored.LodgingView!.RemainingHours);
    }

    [Fact]
    public void Semantic_action_requires_exact_shape_duration_and_admits_zero_cost()
    {
        Assert.NotNull(DaggerfallUiAction.Parse("{\"action\":\"lodging-book\",\"key\":\"1/2/3/4/5\",\"days\":1,\"amount\":0}"u8));
        Assert.Null(DaggerfallUiAction.Parse("{\"action\":\"lodging-book\",\"key\":\"1/2/3/4/5\",\"days\":351,\"amount\":0}"u8));
        Assert.Null(DaggerfallUiAction.Parse("{\"action\":\"lodging-quote\",\"key\":\"1/2/3/4/5\",\"days\":1,\"amount\":0}"u8));
        Assert.Null(DaggerfallUiAction.Parse("{\"action\":\"currency-deposit-gold\",\"amount\":0}"u8));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly DaggerfallDefinitions definitions = TestPayload.Definitions;
        private readonly List<string> releases = [];
        private readonly DaggerfallBlocksSnapshot blocks;
        private readonly DaggerfallSiteProfiles profiles;
        private readonly ResolvedCompositionIdentity identity;
        private readonly DaggerfallSkyMedia sky;
        private ulong step = 1;
        internal DaggerfallSession Session { get; }
        internal DaggerfallSiteProfile Interior { get; }
        internal DaggerfallSiteProfile Exterior { get; }
        internal DaggerfallSiteProfile Other { get; }
        internal Fixture()
        {
            string root = TestData.RepositoryRoot;
            DaggerfallSiteProfile source = ReadInputs(root);
            blocks = DaggerfallBlocksContent.Read(File.ReadAllBytes(Path.Combine(root, "content/worldrpg/payloads/daggerfall.blocks.json")));
            var candidates = definitions.Locations.Records.SelectMany(site => site.Exterior!.Buildings.Values
                .Where(building => building.Source.BuildingType == 15)
                .Select(building => (site.Id, Building: building))).Take(2).ToArray();
            Interior = Profile(candidates[0].Id, candidates[0].Building, "lodging-first", DaggerfallWorldProfileKind.Interior);
            Other = Profile(candidates[1].Id, candidates[1].Building, "lodging-other", DaggerfallWorldProfileKind.Interior);
            Exterior = Profile(candidates[0].Id, null, "lodging-exterior", DaggerfallWorldProfileKind.Exterior);
            profiles = new([Interior, Other, Exterior]);
            identity = GameCompositionResolver.Resolve(FullContent(root), new GameBundleId("daggerfall.classic")).RequireComposition().Identity;
            sky = DaggerfallSkyMedia.Read(DaggerfallSkyMediaTests.Fixture().Content);
            Session = Create(null);
            DaggerfallSiteProfile Profile(DaggerfallSiteId site, DaggerfallSiteBuildingSource? building, string name, DaggerfallWorldProfileKind kind) => new(
                new ProjectFacts(new WorldPoint(1, 1, 1), new Dictionary<long, AuthoredActor>()), source.SpatialArtifact, source.StaticMesh,
                source.WorldAppearance, source.InitialLook, source.Materials, new Dictionary<long, NormalizedActorSprite>(),
                source.MobileSprites, source.Audio, source.ClassicPresentation, site, profileKind: kind, logicalProfileId: name,
                interiorBuilding: building is null ? null : new(building.Id.BlockX, building.Id.BlockY, building.Source.Id, 15, building.Source.FactionId),
                billboardSprites: source.BillboardSprites, terrainTextures: source.TerrainTextures);
        }
        private DaggerfallSession Create(RulesetSavePayload? save)
        {
            ContentFake content = new(releases);
            foreach (var profile in new[] { Interior, Other, Exterior })
            {
                PopulateContent(content, profile);
                PopulateTerrainContent(content, profile);
            }
            EngineContextFake engine = EngineContextFake.Create(content, SpatialFake.Create(Interior.SpatialArtifact.Sha256, releases).Service,
                new AppearanceFake(releases), random: LodgingRandom.Create());
            DaggerfallSessionComposition composition = new(definitions, Interior, DaggerfallTuning.Defaults, identity)
                { Profiles = profiles, Blocks = blocks, Sky = sky };
            return save is null ? DaggerfallSession.StartNew(engine.Context, composition) : DaggerfallSession.Restore(engine.Context, composition, save);
        }
        internal DaggerfallSession Restore(RulesetSavePayload save) => Create(save);
        internal void Submit(object action) => Session.Update(new ProductUpdate(OuterUpdate(step++), [Ui(JsonSerializer.Serialize(action))]));
        internal void Quote(int days) => Submit(new { action = "lodging-quote", key = Session.LodgingView!.Key, days });
        internal void Book(DaggerfallLodgingView quote) => Submit(new { action = "lodging-book", key = quote.Key, days = quote.Days, amount = quote.Price });
        internal void Rest(int hours) => Submit(new { action = "rest", mode = "timed", hours });
        internal void AddGold(ulong quantity)
        {
            var item = definitions.RequireItem(new DaggerfallItemId("gold-piece"));
            var stack = InventoryStackId.Parse("lodging.gold");
            Session.State.Inventory.Grant(new(new InventoryItemId(item.Id.Value), stack, quantity));
            Session.State.ItemInstances.RegisterStack(DaggerfallItemOwner.Player, stack, DaggerfallItemInstanceMetadata.Default(item, DaggerfallItemOwner.Player));
        }
        private static void PopulateTerrainContent(ContentFake content, DaggerfallSiteProfile profile)
        {
            foreach (NormalizedTerrainTexture texture in profile.TerrainTextures.Values)
                content.Add(texture.TexturePath, texture.TextureSha256);
        }
        public void Dispose() => Session.Dispose();
    }
}

internal class LodgingRandom : DispatchProxy
{
    internal static IRandomService Create() => DispatchProxy.Create<IRandomService, LodgingRandom>();
    protected override object? Invoke(MethodInfo? method, object?[]? arguments)
    {
        if (method?.Name == nameof(IRandomService.DrawKeyed))
            return new KeyedRngReceipt(((KeyedRngRequest)arguments![0]!).Maximum);
        if (method?.Name == nameof(IRandomService.DrawLcg15))
        {
            Lcg15Request request = (Lcg15Request)arguments![0]!;
            uint state = unchecked(request.State * 1103515245u + 12345u);
            return new Lcg15Receipt(state, ((state >> 16) & 0x7fffu) % request.UpperExclusive);
        }
        throw new NotSupportedException(method?.Name);
    }
}
