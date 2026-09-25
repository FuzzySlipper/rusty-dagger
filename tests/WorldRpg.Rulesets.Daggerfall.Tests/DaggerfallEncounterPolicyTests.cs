using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Encounters;
using WorldRpg.Rulesets.Daggerfall.Policies;
using Rusty.Engine;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using System.Reflection;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

public sealed class DaggerfallEncounterPolicyTests
{
    [Theory]
    [InlineData(DaggerfallEncounterContext.LocationNight, 224, 20, 24)]
    [InlineData(DaggerfallEncounterContext.WildernessDay, 226, 24, 36)]
    [InlineData(DaggerfallEncounterContext.WildernessNight, 232, 37, 24)]
    public void SelectsEveryExteriorContextFromItsClimateTable(DaggerfallEncounterContext context, int climate, int expectedTable, int denominator)
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallEncounterChoice choice = DaggerfallEncounterPolicy.Choose(
            definitions.Encounters, new DaggerfallEncounterRequest(context, PlayerLevel: 1, Climate: climate), Minimum);

        Assert.Equal(expectedTable, choice.Table);
        Assert.Equal(0, choice.Entry);
        Assert.Equal(definitions.Encounters.Tables[expectedTable][0], choice.MobileId);
        Assert.Equal(denominator, choice.ChanceDenominator);
        Assert.Null(choice.Reason);
    }

    [Fact]
    public void DungeonNeedsAlertAndUsesItsDungeonTypeTable()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallEncounterChoice quiet = DaggerfallEncounterPolicy.Choose(definitions.Encounters,
            new DaggerfallEncounterRequest(DaggerfallEncounterContext.Dungeon, 8, DungeonType: 18), Minimum);
        Assert.Equal("dungeon-not-alert", quiet.Reason);

        DaggerfallEncounterChoice alert = DaggerfallEncounterPolicy.Choose(definitions.Encounters,
            new DaggerfallEncounterRequest(DaggerfallEncounterContext.Dungeon, 8, DungeonType: 18, EnemyAlert: true), Minimum);
        Assert.Equal(18, alert.Table);
        Assert.Equal(3, alert.MobileId);
        Assert.Equal(DaggerfallEncounterPolicy.DungeonDenominator, alert.ChanceDenominator);
    }

    [Theory]
    [InlineData(1, 1, 0, 5)]
    [InlineData(80, 10, 7, 13)]
    [InlineData(81, 10, 0, 11)]
    [InlineData(96, 5, 0, 7)]
    [InlineData(96, 6, 0, 19)]
    [InlineData(80, 30, 14, 19)]
    public void PreservesClassicPlayerLevelBands(int percentile, int level, int minimum, int maximum) =>
        Assert.Equal((minimum, maximum), DaggerfallEncounterPolicy.LevelBand(percentile, level));

    [Fact]
    public void ASourceChanceMissDoesNotChooseOrRollATable()
    {
        List<string> ids = [];
        DaggerfallEncounterChoice miss = DaggerfallEncounterPolicy.Choose(Definitions().Encounters,
            new DaggerfallEncounterRequest(DaggerfallEncounterContext.WildernessDay, 1, Climate: 224),
            (id, minimum, _) =>
            {
                ids.Add(id);
                return minimum + 1;
            });
        Assert.Equal("source-chance-missed", miss.Reason);
        Assert.Equal(["chance"], ids);
        Assert.Null(miss.MobileId);
    }

    [Theory]
    [InlineData(DaggerfallEncounterContext.Dungeon, null, null)]
    [InlineData((DaggerfallEncounterContext)99, 224, null)]
    [InlineData(DaggerfallEncounterContext.WildernessDay, 999, null)]
    public void InvalidRequestsFailBeforeTheyDraw(DaggerfallEncounterContext context, int? climate, int? dungeonType)
    {
        int draws = 0;
        Assert.ThrowsAny<ArgumentException>(() => DaggerfallEncounterPolicy.Choose(
            Definitions().Encounters,
            new DaggerfallEncounterRequest(context, 1, climate, dungeonType),
            (_, _, _) => { draws++; return 0; }));
        Assert.Equal(0, draws);
    }

    [Fact]
    public void RetainsTheCompleteFortyFiveTableCorpusAndEveryClassMobile()
    {
        DaggerfallEncounterSet encounters = Definitions().Encounters;
        Assert.Equal(45, encounters.Tables.Count);
        Assert.All(encounters.Tables, table => Assert.Equal(20, table.Count));
        Assert.Contains(128, encounters.Tables.SelectMany(table => table));
        Assert.Contains(145, encounters.Tables.SelectMany(table => table));
    }

    [Fact]
    public void PersistsASelectedClassEncounterBeforeItMaterializesWithoutAnotherDraw()
    {
        DaggerfallDefinitions definitions = TestPayload.Definitions;
        RecordingRandom recording = RecordingRandom.Create();
        DaggerfallEncounterRuntime first = new(definitions, recording.Service);
        ActorPose pose = new(new WorldPoint(3, 4, 5), 0.5f);
        // Dungeon table two begins with Warrior mobile 144, one of the classes the encounter actor
        // projection supplies from class16 rather than substituting the thief.
        DaggerfallEncounterResolution selected = first.Select(
            new DaggerfallEncounterRequest(DaggerfallEncounterContext.Dungeon, 1, DungeonType: 2, EnemyAlert: true), 4, "privateers-hold", pose);
        Assert.Equal(144, selected.Choice.MobileId);
        Assert.Equal("encounter-warrior", selected.ActorDefinition);
        Assert.NotNull(definitions.RequireActor(new DaggerfallActorId(selected.ActorDefinition!)));
        Assert.Equal(3, recording.Requests.Count);

        DaggerfallEncounterRuntime restored = new(definitions, recording.Service);
        restored.Restore(first.Capture(), new Dictionary<long, string>());
        long[] spawned = restored.MaterializePending("privateers-hold", (definition, actualPose, level) =>
        {
            Assert.Equal("encounter-warrior", definition);
            Assert.Equal(pose, actualPose);
            Assert.Equal(1, level);
            return 7_000;
        }).ToArray();
        Assert.Equal([7_000L], spawned);
        Assert.Equal(3, recording.Requests.Count);
        Assert.Empty(restored.MaterializePending("privateers-hold", (_, _, _) => throw new Xunit.Sdk.XunitException("already spawned")));
        Assert.Equal(7_000, Assert.Single(restored.Resolved).SpawnedActorId);
    }

    [Fact]
    public void RejectsRestoredSelectionsThatDoNotMatchTheCanonicalTableOrSpawnedActor()
    {
        DaggerfallDefinitions definitions = Definitions();
        DaggerfallEncounterRuntime runtime = new(definitions, RecordingRandom.Create().Service);
        DaggerfallEncounterResolution selected = runtime.Select(
            new DaggerfallEncounterRequest(DaggerfallEncounterContext.Dungeon, 1, DungeonType: 2, EnemyAlert: true), 4, "privateers-hold",
            new ActorPose(new WorldPoint(3, 4, 5), 0.5f));
        DaggerfallEncounterRuntimeSave wrongTable = new([selected with { Choice = selected.Choice with { Entry = 1 } }]);
        Assert.Throws<ArgumentException>(() => new DaggerfallEncounterRuntime(definitions, RecordingRandom.Create().Service).Restore(wrongTable, new Dictionary<long, string>()));

        DaggerfallEncounterRuntimeSave missingActor = new([selected with { SpawnedActorId = 7_000 }]);
        Assert.Throws<ArgumentException>(() => new DaggerfallEncounterRuntime(definitions, RecordingRandom.Create().Service).Restore(missingActor, new Dictionary<long, string>()));
        DaggerfallEncounterRuntime accepted = new(definitions, RecordingRandom.Create().Service);
        accepted.Restore(missingActor, new Dictionary<long, string> { [7_000] = "encounter-warrior" });
        Assert.Equal(7_000, Assert.Single(accepted.Resolved).SpawnedActorId);
        DaggerfallEncounterRuntime retired = new(definitions, RecordingRandom.Create().Service);
        retired.Restore(missingActor, new Dictionary<long, string>(), new HashSet<long> { 7_000 });
        Assert.Equal(7_000, Assert.Single(retired.Resolved).SpawnedActorId);
        Assert.Throws<ArgumentException>(() => new DaggerfallEncounterRuntime(definitions, RecordingRandom.Create().Service)
            .Restore(missingActor, new Dictionary<long, string>(), new HashSet<long> { 7_001 }));

        DaggerfallEncounterRuntimeSave wrongActor = new([selected with { SpawnedActorId = 7_000 }]);
        Assert.Throws<ArgumentException>(() => new DaggerfallEncounterRuntime(definitions, RecordingRandom.Create().Service)
            .Restore(wrongActor, new Dictionary<long, string> { [7_000] = "imp" }));
        DaggerfallEncounterRuntimeSave sequenceGap = new([selected with { Sequence = 2 }]);
        Assert.Throws<ArgumentException>(() => new DaggerfallEncounterRuntime(definitions, RecordingRandom.Create().Service)
            .Restore(sequenceGap, new Dictionary<long, string>()));
    }

    [Fact]
    public void PendingEncounterOnlyMaterializesInItsOriginatingProfile()
    {
        DaggerfallEncounterRuntime runtime = new(Definitions(), RecordingRandom.Create().Service);
        runtime.Select(new DaggerfallEncounterRequest(DaggerfallEncounterContext.Dungeon, 1, DungeonType: 2, EnemyAlert: true), 1, "privateers-hold", new(new WorldPoint(0, 0, 0), 0));
        Assert.Empty(runtime.MaterializePending("castle-necromoghan", (_, _, _) => throw new Xunit.Sdk.XunitException("wrong profile")));
        Assert.Single(runtime.MaterializePending("privateers-hold", (_, _, _) => 8_000));
    }

    private static int Minimum(string id, int minimum, int maximum) => minimum;

    private static DaggerfallDefinitions Definitions() => TestPayload.Definitions;

    private class RecordingRandom : DispatchProxy
    {
        internal IRandomService Service { get; private set; } = null!;
        internal List<KeyedRngRequest> Requests { get; } = [];
        internal static RecordingRandom Create()
        {
            IRandomService service = DispatchProxy.Create<IRandomService, RecordingRandom>();
            RecordingRandom proxy = (RecordingRandom)(object)service;
            proxy.Service = service;
            return proxy;
        }

        protected override object? Invoke(MethodInfo? method, object?[]? arguments)
        {
            if (method?.Name != nameof(IRandomService.DrawKeyed)) throw new NotSupportedException(method?.Name);
            KeyedRngRequest request = (KeyedRngRequest)arguments![0]!;
            Requests.Add(request);
            return new KeyedRngReceipt(request.Minimum);
        }
    }
}
