using System.Numerics;
using Rusty.Engine;
using Rusty.Engine.Debugging;
using Rusty.Engine.Entities;
using WorldRpg.Kit;
using WorldRpg.Kit.Actors;
using WorldRpg.Kit.Controls;
using WorldRpg.Kit.World;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// <see cref="DaggerfallDeveloperScene"/> built without the session: world time, a recording "pass this much time" and a
/// recording art switch as plain inputs. The weather, horizon and ground owners it sets are
/// <c>DeveloperWorldCommandsTests</c>' and the Exterior tests' end-to-end cases.
/// </summary>
public sealed class DaggerfallDeveloperSceneTests
{
    private readonly List<long> _passed = [];
    private readonly List<bool> _classic = [];
    private string? _refusal;
    private readonly DaggerfallWorldTime _time = new(DaggerfallCalendar.Start, 0d, 12d);

    [Fact]
    public void An_hour_or_a_season_lets_time_run_on_to_it()
    {
        DaggerfallDeveloperScene scene = Scene();
        long secondOfDay = (long)_time.Calendar.SecondOfDay;
        Assert.True(scene.Hour(9).Succeeded);
        Assert.Equal([(9 * 3_600 - secondOfDay + 86_400) % 86_400], _passed);
        Assert.Equal("An hour is from 0 to 23.", scene.Hour(24).Message);

        DaggerfallSeason now = _time.Calendar.Season;
        Assert.Equal($"It is already {now}.", scene.Season(now.ToString().ToLowerInvariant()).Message);
        Assert.Equal("A season is one of Winter, Spring, Summer, Autumn.", scene.Season("Monsoon").Message);
        DaggerfallSeason next = Enum.GetValues<DaggerfallSeason>().First(season => season != now);
        Assert.True(scene.Season(next.ToString()).Succeeded);
        Assert.Equal(2, _passed.Count);
        Assert.True(_passed[1] > 0 && _passed[1] % DaggerfallCalendar.SecondsPerDay == 0);
    }

    [Fact]
    public void Classic_art_is_switched_for_the_run_and_settings_out_of_range_are_refused()
    {
        DaggerfallDeveloperScene scene = Scene();
        Assert.Equal("Classic art is drawn in place of the refreshed art for this run.", scene.ClassicArt("on").Message);
        Assert.Equal("The refreshed art is drawn.", scene.ClassicArt("off").Message);
        Assert.Equal("Classic art is on or off.", scene.ClassicArt("maybe").Message);
        Assert.Equal([true, false], _classic);
        // A switch the session cannot make, as a classic file absent, is refused with its reason.
        _refusal = "Published content 'worldrpg/media/effects/effect-blood-0-atlas.png' could not be opened.";
        DebugCommandResult refused = scene.ClassicArt("on");
        Assert.False(refused.Succeeded);
        Assert.Equal($"Classic art cannot be drawn: {_refusal}", refused.Message);

        Assert.Equal("Trees grow as mesh or sprite.", scene.Trees("bush").Message);
        Assert.Equal("The far forest is on or off.", scene.FarForest("maybe").Message);
        Assert.Equal("A density is finite and 0 or more.", scene.Flora(-1).Message);
        Assert.Equal("A lift is 1 (true height) or more.", scene.HorizonSet(0, 1).Message);
        Assert.Equal("A haze is a positive density per metre.", scene.HorizonSet(2, 0).Message);
        Assert.Equal("A contrast is 1 or more, a repeat above 0 and a sharpness 0 or more.", scene.GroundBlend(0, 1, 1).Message);
        Assert.StartsWith("The weather is one of ", scene.Weather("Hail").Message);
        foreach ((string edge, string erosion) in new[] {("-1", "0"), ("0", "NaN"), ("sharp", "tuning")})
            Assert.Equal("An edge sharpness and an erosion are each a finite number, 0 or more, or tuning.", scene.CloudEdges(edge, erosion).Message);
    }

    private DaggerfallDeveloperScene Scene() => new(_time, _passed.Add, null!, null!, null!, null!, new PlayerControlState(null, 0f, 0f),
        TestPayload.Definitions, classic => { _classic.Add(classic); return _refusal; }, () => null);
}
