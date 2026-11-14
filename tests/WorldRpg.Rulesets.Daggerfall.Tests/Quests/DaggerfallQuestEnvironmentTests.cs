using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The quest environment built without the session: the weather, the climate and the site's audio are plain inputs, and
/// no music, cinematics or videos are composed.
/// </summary>
public sealed class DaggerfallQuestEnvironmentTests
{
    internal static DaggerfallQuestRuntimeInstance Instance(string action = "end quest")
    {
        DaggerfallQuestSourceDefinition source = new("owner-test", "", "owner-test.txt", DaggerfallQuestDisposition.Compiled, [],
            [new("headless", 1, [action], null)], []);
        DaggerfallQuestTaskProgram program = DaggerfallQuestTaskCompiler.Compile(source);
        return new(new DaggerfallQuestInstanceSave("quest:1", source.SourceFile, source.Name, DaggerfallQuestLifecycle.Active, null, [], [])
            { Tasks = DaggerfallQuestTaskCompiler.InitialState(program) }, program);
    }

    internal static DaggerfallQuestTaskOperation Operation(DaggerfallQuestTaskOperationKind kind, params string[] targets) =>
        new(kind, 1, kind + " " + string.Join(' ', targets), targets, [], null);

    private static DaggerfallQuestEnvironment Environment(ConditionSessionFixture fixture, DaggerfallWeatherKind weather, int climate) =>
        new(fixture.Definitions, new DaggerfallWorldTime(new(405, 0, 0, 0, 0, 0), 0d, 12d),
            new DaggerfallQuestDiseases(fixture.Definitions, fixture.Session.Diseases, fixture.Session.State.Effects, fixture.Session.State.Actors.Player.DurableId),
            music: null, musicBundle: null, videosEnabled: false, cinematics: null, audio: () => [], weather: () => weather, currentClimate: () => climate,
            playSound: (_, _, _, _) => throw new InvalidOperationException("No quest sound is admitted."));

    [Fact]
    public void Season_weather_and_climate_are_read_as_they_are_asked()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallQuestEnvironment environment = Environment(fixture, DaggerfallWeatherKind.Rain, 225);

        Assert.True(environment.Condition(Operation(DaggerfallQuestTaskOperationKind.Season, "winter"), new(405, 0, 0, 0, 0, 0)));
        Assert.False(environment.Condition(Operation(DaggerfallQuestTaskOperationKind.Season, "winter"), new(405, 5, 0, 0, 0, 0)));
        Assert.True(environment.Condition(Operation(DaggerfallQuestTaskOperationKind.Weather, "rain"), DaggerfallCalendar.Start));
        Assert.False(environment.Condition(Operation(DaggerfallQuestTaskOperationKind.Weather, "snow"), DaggerfallCalendar.Start));
        Assert.True(environment.Condition(Operation(DaggerfallQuestTaskOperationKind.Climate, "base desert"), DaggerfallCalendar.Start));
        Assert.True(environment.Condition(Operation(DaggerfallQuestTaskOperationKind.Climate, "desert2"), DaggerfallCalendar.Start));
        Assert.False(environment.Condition(Operation(DaggerfallQuestTaskOperationKind.Climate, "swamp"), DaggerfallCalendar.Start));
        Assert.Throws<NotSupportedException>(() => environment.Condition(Operation(DaggerfallQuestTaskOperationKind.Climate, "moon"), DaggerfallCalendar.Start));
    }

    [Fact]
    public void Media_with_no_admitted_asset_is_not_offered_and_a_disabled_video_counts_as_played()
    {
        using ConditionSessionFixture fixture = new();
        DaggerfallQuestEnvironment environment = Environment(fixture, DaggerfallWeatherKind.Sunny, 231);
        DaggerfallQuestRuntimeInstance instance = Instance();
        string disease = fixture.Definitions.QuestSources.Tables.Diseases.Lookup.First(pair => pair.Value == 0).Key;

        Assert.False(environment.OfferCapability(Operation(DaggerfallQuestTaskOperationKind.PlayVideo, "1")));
        Assert.False(environment.OfferCapability(Operation(DaggerfallQuestTaskOperationKind.PlaySong, "song_00")));
        Assert.False(environment.OfferCapability(Operation(DaggerfallQuestTaskOperationKind.PlaySound, "unlisted", "0", "0")));
        Assert.True(environment.OfferCapability(Operation(DaggerfallQuestTaskOperationKind.MakePcDiseased, disease)));
        Assert.False(environment.OfferCapability(Operation(DaggerfallQuestTaskOperationKind.MakePcDiseased, "MissingDisease")));
        Assert.True(environment.OfferCapability(Operation(DaggerfallQuestTaskOperationKind.Season, "fall")));

        Assert.True(environment.Media(instance, Operation(DaggerfallQuestTaskOperationKind.PlayVideo, "1"), instance.Tasks[0], 0, DaggerfallCalendar.Start));
        Assert.Throws<NotSupportedException>(() =>
            environment.Media(instance, Operation(DaggerfallQuestTaskOperationKind.PlaySong, "song_00"), instance.Tasks[0], 0, DaggerfallCalendar.Start));
    }
}
