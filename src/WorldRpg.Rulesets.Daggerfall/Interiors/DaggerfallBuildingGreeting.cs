using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

/// <summary>
/// The greeting a shop or a guild hall gives as the player walks in by its door, and the entry it holds until the
/// player has read it. Its draw counts greetings for the session's life, so it is built once, not with each site.
/// </summary>
internal sealed class DaggerfallBuildingGreeting(DaggerfallDefinitions definitions, IRandomService random)
{
    private (DaggerfallRdbDoorId Door, DaggerfallActivationMode? ForceMode)? _held;
    private ulong _sequence;

    /// <summary>
    /// The greeting this building gives the player entering by a door, holding the entry until it is read; null when
    /// it gives none. <paramref name="profile"/> is the active profile's logical id, which keys the guild hall's draw.
    /// </summary>
    internal DaggerfallDungeonTextProjection? Present(DaggerfallRdbDoorId door, DaggerfallActivationMode? forceMode,
        DaggerfallSiteBuildingSource building, bool closed, bool owned, string profile, Func<DaggerfallTextContext> textContext)
    {
        int type = building.Source.BuildingType;
        int? resource = !closed && !owned && forceMode is null && type is >= 17 and <= 20
            && building.Source.FactionId is not (42 or 108) ? 256
            : DaggerfallNpcServiceFacts.IsShop(type) ? building.Quality switch
            { <= 3 => 270, <= 7 => 269, <= 13 => 268, <= 17 => 267, _ => 266 } : null;
        if (resource is null) return null;
        DaggerfallTextKey key = new(DaggerfallTextKind.Resource, resource.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string text;
        if (resource == 256)
        {
            var choices = definitions.Text.Require(key).TextRuns.ToArray();
            int choice = checked((int)random.DrawKeyed(new KeyedRngRequest(0x4752454554UL, "daggerfall.building-greeting",
                $"{profile}:{door}:{_sequence}", 0, choices.Length - 1)).Value);
            text = definitions.TextPresentation.ResolveRaw(choices[choice], key, textContext()).Text;
        }
        else text = definitions.TextPresentation.Resolve(key, textContext()).Text;
        _held = (door, forceMode);
        return new($"building-entry/{door}", DaggerfallDungeonTextActionKind.BuildingGreeting,
            resource.Value, text, [], ++_sequence, false, false);
    }

    /// <summary>The entry the greeting held, now released to go ahead; null when none was held.</summary>
    internal (DaggerfallRdbDoorId Door, DaggerfallActivationMode? ForceMode)? TakeHeldEntry()
    {
        var held = _held;
        _held = null;
        return held;
    }

    /// <summary>Drops a held entry without entering, as the site unloads.</summary>
    internal void Release() => _held = null;
}
