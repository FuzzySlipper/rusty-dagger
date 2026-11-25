using Rusty.Engine.Debugging;
using WorldRpg.Kit;
using WorldRpg.Kit.Controls;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Loot;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using static WorldRpg.Rulesets.Daggerfall.DaggerfallDeveloperReplies;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// Developer settings of the world around the player for this run (world.hour, season, weather, ground-weather,
/// ground-blend, horizon, horizon-set, cloud-edges, flora, farforest, trees, groundcover, river, ground-survey,
/// classic-art). Time runs on through the session's passing of time, as resting would; the rest sets the weather,
/// cloud, horizon, ground and art owners as their own settings do, and nothing of it is saved.
/// </summary>
/// <param name="passTime">Lets this many game seconds pass (the session's AdvanceElapsedTime, without idle fatigue).</param>
/// <param name="useClassicArt">
/// Draws the classic art in place of the refreshed art (true) or the refreshed art (false); answers why it cannot, as a
/// classic file absent, or null.
/// </param>
/// <param name="hanging">The active site's hanging pieces, when it has an owner for them.</param>
/// <param name="looseItems">The loose items' bodies.</param>
internal sealed class DaggerfallDeveloperScene(DaggerfallWorldTime time, Action<long> passTime, DaggerfallHorizon horizon,
    DaggerfallSiteCrossing sites, DaggerfallWeather weather, DaggerfallWeatherPresentation weatherPresentation, PlayerControlState player,
    DaggerfallDefinitions definitions, Func<bool, string?> useClassicArt, Func<DaggerfallHanging?> hanging,
    DaggerfallLooseItems looseItems, DaggerfallInteriorLook? interiorLook = null, Action? syncLook = null) : IDeveloperSceneCommands
{
    public DebugCommandResult LooseItems() => DebugCommandResult.Success(looseItems.Describe());

    public DebugCommandResult Hanging() => hanging() is { } pieces
        ? DebugCommandResult.Success(pieces.Describe())
        : DebugCommandResult.Success("Nothing hangs here: hanging pieces swing only in dungeons and interiors.");

    public DebugCommandResult Hour(int hour)
    {
        if (hour is < 0 or > 23) return Refused("An hour is from 0 to 23.");
        const int secondsPerDay = 86_400;
        int seconds = ((hour * 3_600) - (int)time.Calendar.SecondOfDay + secondsPerDay) % secondsPerDay;
        passTime(seconds);
        return DebugCommandResult.Success($"It is {time.Calendar.SecondOfDay / 3_600}:00, {time.Calendar.Season}.");
    }

    public DebugCommandResult Season(string season)
    {
        if (!Enum.TryParse(season, ignoreCase: true, out DaggerfallSeason target) || !Enum.IsDefined(target))
            return Refused($"A season is one of {string.Join(", ", Enum.GetNames<DaggerfallSeason>())}.");
        DaggerfallCalendar now = time.Calendar;
        if (now.Season == target) return DebugCommandResult.Success($"It is already {target}.");
        // Each season opens on its first month, by the calendar's own table.
        int first = target switch { DaggerfallSeason.Winter => 11, DaggerfallSeason.Spring => 2, DaggerfallSeason.Summer => 5, _ => 8 };
        int days = ((first - now.Month + DaggerfallCalendar.MonthsPerYear) % DaggerfallCalendar.MonthsPerYear * DaggerfallCalendar.DaysPerMonth) - now.Day;
        passTime((long)days * DaggerfallCalendar.SecondsPerDay);
        return DebugCommandResult.Success($"It is day {time.Calendar.Day + 1} of month {time.Calendar.Month + 1}, {time.Calendar.Season}.");
    }

    public DebugCommandResult GroundBlend(double contrast, double repeatMetres, double triplanarSharpness)
    {
        if (contrast < 1 || repeatMetres <= 0 || triplanarSharpness < 0) return Refused("A contrast is 1 or more, a repeat above 0 and a sharpness 0 or more.");
        if (!DaggerfallGroundBlend.FillsCell((float)repeatMetres)) return Refused($"A texture repeating every {repeatMetres} metres would break at every cell's edge.");
        horizon.Rebuild();
        sites.Exterior.UseGroundBlend(new((float)contrast, (float)repeatMetres, (float)triplanarSharpness));
        return DebugCommandResult.Success("Ground layers again by these settings.");
    }

    public DebugCommandResult GroundWeather(double wet, double snow)
    {
        weather.State.Force((float)wet, (float)snow);
        return DebugCommandResult.Success($"The ground here is {weather.Conditions.Wet:0.##} wet and {weather.Conditions.Snow:0.##} snowed.");
    }

    public DebugCommandResult Weather(string kind)
    {
        if (!Enum.TryParse(kind, ignoreCase: true, out DaggerfallWeatherKind chosen) || !Enum.IsDefined(chosen))
            return Refused($"The weather is one of {string.Join(", ", Enum.GetNames<DaggerfallWeatherKind>())}.");
        weather.State.Force(chosen);
        return DebugCommandResult.Success($"The weather is {chosen}.");
    }

    public DebugCommandResult NearestRiver() => sites.Exterior.ActiveExteriorOrigin is { } origin && player.Position is { } position
        ? DebugCommandResult.Success(horizon.NearestRiver(origin, position.ToVector(), definitions.Terrain))
        : Refused("The player is not outdoors.");

    public DebugCommandResult GroundSurvey() => DebugCommandResult.Success(horizon.GroundSurvey(definitions.Terrain));

    public DebugCommandResult Horizon() => DebugCommandResult.Success(horizon.Readout());

    public DebugCommandResult HorizonSet(double lift, double fogDensity)
    {
        if (!double.IsFinite(lift) || lift < 1) return Refused("A lift is 1 (true height) or more.");
        if (!double.IsFinite(fogDensity) || fogDensity <= 0) return Refused("A haze is a positive density per metre.");
        horizon.Adjust(lift, fogDensity);
        return DebugCommandResult.Success(horizon.Readout());
    }

    public DebugCommandResult CloudEdges(string edge, string erosion)
    {
        if (!TryEdge(edge, out float? sharpness) || !TryEdge(erosion, out float? eroded))
            return Refused("An edge sharpness and an erosion are each a finite number, 0 or more, or tuning.");
        weatherPresentation.UseCloudEdges(sharpness, eroded);
        (float drawnSharpness, float drawnErosion) = weatherPresentation.CloudEdges;
        return DebugCommandResult.Success($"Raymarched clouds draw at edge sharpness {drawnSharpness:0.##} and erosion {drawnErosion:0.##}.");

        static bool TryEdge(string text, out float? value)
        {
            value = null;
            if (text == "tuning") return true;
            if (!float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsed)
                || !float.IsFinite(parsed) || parsed < 0) return false;
            value = parsed;
            return true;
        }
    }

    public DebugCommandResult Flora(double density)
    {
        if (!double.IsFinite(density) || density < 0) return Refused("A density is finite and 0 or more.");
        horizon.UseFloraDensity((float)density);
        return DebugCommandResult.Success(horizon.Readout());
    }

    public DebugCommandResult FarForest(string state)
    {
        if (state is not ("on" or "off")) return Refused("The far forest is on or off.");
        horizon.UseFarForest(state == "on");
        return DebugCommandResult.Success($"The far forest is {state}.");
    }

    public DebugCommandResult Trees(string kind)
    {
        if (kind is not ("mesh" or "sprite")) return Refused("Trees grow as mesh or sprite.");
        horizon.UseTreeMeshes(kind == "mesh");
        return DebugCommandResult.Success($"Trees grow as {kind}s.");
    }

    public DebugCommandResult TreeBodies(string state)
    {
        if (state is not ("on" or "off")) return Refused("Tree bodies are on or off.");
        horizon.UseTreeBodies(state == "on");
        return DebugCommandResult.Success($"Tree bodies are {state}.");
    }

    public DebugCommandResult ClassicArt(string state)
    {
        if (state is not ("on" or "off")) return Refused("Classic art is on or off.");
        bool classic = state == "on";
        if (useClassicArt(classic) is { } refusal) return Refused($"Classic art cannot be drawn: {refusal}");
        return DebugCommandResult.Success(classic ? "Classic art is drawn in place of the refreshed art for this run." : "The refreshed art is drawn.");
    }

    public DebugCommandResult GroundCover(string kinds)
    {
        if (horizon.GroundCover is not { } cover) return Refused("Ground cover draws outdoors in refreshed art.");
        string[] named = kinds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (named.Length == 0 || named.Any(kind => kind is not ("grass" or "relief" or "tufts" or "all" or "none")))
            return Refused("Ground cover is grass, relief and tufts, separated by commas, or all or none.");
        bool all = named.Contains("all");
        cover.Show(all || named.Contains("grass"), all || named.Contains("relief"), all || named.Contains("tufts"));
        return DebugCommandResult.Success($"Ground cover: {kinds}.");
    }

    public DebugCommandResult InteriorLook(string preset)
    {
        if (interiorLook is null) return Refused("Interior look controls are unavailable in this session.");
        string trimmed = preset.Trim().ToLowerInvariant();
        if (trimmed is "status" or "list") return DebugCommandResult.Success(interiorLook.Describe());
        if (interiorLook.Select(trimmed) is { } refusal) return Refused(refusal);
        syncLook?.Invoke();
        return DebugCommandResult.Success(interiorLook.Describe());
    }

    public DebugCommandResult InteriorRelief(string mode)
    {
        if (interiorLook is null) return Refused("Interior relief controls are unavailable in this session.");
        string trimmed = mode.Trim().ToLowerInvariant();
        if (trimmed is "status" or "list") return DebugCommandResult.Success(interiorLook.DescribeRelief());
        if (interiorLook.SelectRelief(trimmed) is { } refusal) return Refused(refusal);
        syncLook?.Invoke();
        return DebugCommandResult.Success(interiorLook.DescribeRelief());
    }

    public DebugCommandResult InteriorSurfaces(string mode)
    {
        if (interiorLook is null) return Refused("Interior surface controls are unavailable in this session.");
        string trimmed = mode.Trim().ToLowerInvariant();
        if (trimmed is "status" or "list") return DebugCommandResult.Success(interiorLook.DescribeSurfaces());
        if (interiorLook.SelectSurfaces(trimmed) is { } refusal) return Refused(refusal);
        syncLook?.Invoke();
        return DebugCommandResult.Success(interiorLook.DescribeSurfaces());
    }
}
