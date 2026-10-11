using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private (DaggerfallRdbDoorId Door, DaggerfallActivationMode? ForceMode)? _pendingBuildingEntry;
    private ulong _buildingGreetingSequence;

    private bool PresentBuildingGreeting(DaggerfallRdbDoorId door, DaggerfallActivationMode? forceMode, bool closed, bool owned)
    {
        if (!TryExteriorBuilding(door, out _, out var building)) return false;
        int type = building.Source.BuildingType;
        int? resource = !closed && !owned && forceMode is null && type is >= 17 and <= 20
            && building.Source.FactionId is not (42 or 108) ? 256
            : DaggerfallNpcServiceFacts.IsShop(type) ? building.Quality switch
            { <= 3 => 270, <= 7 => 269, <= 13 => 268, <= 17 => 267, _ => 266 } : null;
        if (resource is null) return false;
        DaggerfallTextKey key = new(DaggerfallTextKind.Resource, resource.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        string text;
        if (resource == 256)
        {
            var choices = _definitions.Text.Require(key).TextRuns.ToArray();
            int choice = checked((int)_random.DrawKeyed(new KeyedRngRequest(0x4752454554UL, "daggerfall.building-greeting",
                $"{_activeProfileKey.LogicalId}:{door}:{_buildingGreetingSequence}", 0, choices.Length - 1)).Value);
            text = _definitions.TextPresentation.ResolveRaw(choices[choice], key, CurrentTextContext()).Text;
        }
        else text = _definitions.TextPresentation.Resolve(key, CurrentTextContext()).Text;
        _pendingBuildingEntry = (door, forceMode);
        _dungeonTextProjection = new($"building-entry/{door}", DaggerfallDungeonTextActionKind.BuildingGreeting,
            resource.Value, text, [], ++_buildingGreetingSequence, false, false);
        return true;
    }

    private void ContinueBuildingEntry()
    {
        var pending = _pendingBuildingEntry;
        _pendingBuildingEntry = null;
        _dungeonTextProjection = null;
        if (pending is { } entry && EnterExteriorBuilding(entry.Door, entry.ForceMode, skipGreeting: true) is { } result)
            Presentation.SetOutcome(result.Message);
    }
}
